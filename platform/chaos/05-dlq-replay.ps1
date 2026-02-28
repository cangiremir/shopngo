[CmdletBinding()]
param(
    [string]$QueueName = 'stock.order-created.dlq',
    [string]$TargetExchange = 'ecommerce.events',
    [string]$RabbitApiBase = 'http://localhost:15672/api',
    [string]$RabbitUser = 'guest',
    [string]$RabbitPassword = 'guest',
    [switch]$PurgeQueueAfterReplay,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Common.ps1"
Write-Section "DLQ Replay Drill"

function TryGetReplayCandidate {
    param(
        [Parameter(Mandatory)][string[]]$QueueCandidates,
        [Parameter(Mandatory)][string]$AckMode
    )

    foreach ($candidate in $QueueCandidates) {
        try {
            Write-Step "Reading one message from queue '$candidate'"
            $response = Get-RabbitQueueMessages -QueueName $candidate -AckMode $AckMode -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
            $candidateMessages = if ($null -eq $response) {
                @()
            }
            elseif ($response -is [System.Array]) {
                $response
            }
            else {
                @($response)
            }

            $candidateCount = @($candidateMessages).Count
            if ($candidateCount -eq 0) {
                Write-Warn "No messages found in '$candidate'."
                continue
            }

            $first = @($candidateMessages)[0]
            if (-not $first.PSObject.Properties['routing_key']) {
                Write-Warn "Queue '$candidate' returned an unexpected payload shape. Skipping."
                continue
            }

            return [pscustomobject]@{
                QueueName = $candidate
                Message   = $first
            }
        }
        catch {
            Write-Warn "Unable to read queue '$candidate': $($_.Exception.Message)"
        }
    }

    return $null
}

if ($DryRun) {
    Write-Step "Dry run: would read one message from '$QueueName', inspect properties, and republish to '$TargetExchange'."
    if ($PurgeQueueAfterReplay) {
        Write-Info "[DryRun] Would purge queue '$QueueName' after replay."
    }
    Write-Ok "Dry run completed."
    return
}

$ackMode = 'ack_requeue_false'
$queueCandidates = @($QueueName)
if ($QueueName -eq 'stock.order-created.dlq') {
    $queueCandidates += 'stock.order-created_error'
}

Write-Step "Looking for replayable message in candidate queues"
$candidate = TryGetReplayCandidate -QueueCandidates $queueCandidates -AckMode $ackMode
if ($null -eq $candidate) {
    throw "No replayable messages found in queues: $($queueCandidates -join ', ')."
}

$selectedQueue = [string]$candidate.QueueName
$msg = $candidate.Message
$routingKey = [string]$msg.routing_key
if ([string]::IsNullOrWhiteSpace($routingKey)) {
    throw "Replay message from '$selectedQueue' is missing routing_key."
}

$properties = if ($null -ne $msg.properties) { $msg.properties } else { @{} }
$headers = @{}
if ($null -ne $properties.headers) {
    foreach ($p in $properties.headers.PSObject.Properties) {
        $headers[$p.Name] = $p.Value
    }
}

$messageId = if ($properties.message_id) { [string]$properties.message_id } else { [Guid]::NewGuid().ToString('N') }
$correlationId = if ($properties.correlation_id) { [string]$properties.correlation_id } else { $messageId }
$contentType = if ($properties.content_type) { [string]$properties.content_type } else { 'application/json' }
$payloadEncoding = if ($msg.payload_encoding) { [string]$msg.payload_encoding } else { 'string' }
$payload = [string]$msg.payload

Write-Info "RoutingKey: $routingKey"
Write-Info "MessageId: $messageId"
Write-Info "CorrelationId: $correlationId"
Write-Info "PayloadEncoding: $payloadEncoding"
Write-Info "SourceQueue: $selectedQueue"

Write-Step "Replaying message to exchange '$TargetExchange'"
$publish = Publish-RabbitMessage -Exchange $TargetExchange -RoutingKey $routingKey -Payload $payload -PayloadEncoding $payloadEncoding -MessageId $messageId -CorrelationId $correlationId -Headers $headers -ContentType $contentType -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
Assert-True -Condition ([bool]$publish.routed) -Message "Replay publish was not routed."

if ($PurgeQueueAfterReplay) {
    Write-Step "Purging queue '$selectedQueue' after replay (optional cleanup)"
    Purge-RabbitQueue -QueueName $selectedQueue -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
}

Write-Ok "DLQ replay completed."
