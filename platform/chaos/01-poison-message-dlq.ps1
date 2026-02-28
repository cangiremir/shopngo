[CmdletBinding()]
param(
    [string]$RabbitApiBase = 'http://localhost:15672/api',
    [string]$PrometheusBase = 'http://localhost:9090',
    [string]$RabbitUser = 'guest',
    [string]$RabbitPassword = 'guest',
    [int]$TimeoutSeconds = 300,
    [switch]$SkipAlertCheck,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Common.ps1"
Write-Section "Poison Message Fault Queue Drill"

$queueCandidates = @(
    'stock.order-created.dlq',
    'stock.order-created_error'
)
$routingKey = 'order.created'
$exchange = 'ecommerce.events'
$messageId = [Guid]::NewGuid().ToString('N')

if ($DryRun) {
    Write-Step "Dry run: would read baseline for candidate queues, publish malformed $routingKey, wait for fault queue increment, and optionally wait for alert."
    Write-Ok "Dry run completed."
    return
}

Write-Step "Reading baseline fault queue counts"
$baselines = @{}
foreach ($queue in $queueCandidates) {
    try {
        $count = Get-RabbitQueueCount -QueueName $queue -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
        $baselines[$queue] = $count
        Write-Info ("Baseline {0}: {1}" -f $queue, $count)
    }
    catch {
        # Queue may not exist depending on transport behavior/configuration.
        $baselines[$queue] = $null
    }
}

Write-Step "Publishing malformed JSON to $exchange ($routingKey)"
$publishResult = Publish-RabbitMessage -Exchange $exchange -RoutingKey $routingKey -Payload '{"broken":true' -MessageId $messageId -CorrelationId $messageId -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
Assert-True -Condition ([bool]$publishResult.routed) -Message "RabbitMQ management publish API reported message was not routed."

Write-Step "Waiting for fault queue count to increase"
$hitQueue = $null
$finalCount = $null
Wait-Until -TimeoutSeconds $TimeoutSeconds -Description "fault queue increment" -Condition {
    foreach ($queue in $queueCandidates) {
        $baseline = $baselines[$queue]
        if ($null -eq $baseline) {
            continue
        }

        try {
            $current = Get-RabbitQueueCount -QueueName $queue -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
            if ($current -ge ($baseline + 1)) {
                $script:hitQueue = $queue
                $script:finalCount = $current
                return $true
            }
        }
        catch {
            continue
        }
    }

    return $false
}

Write-Info "Fault queue incremented: $hitQueue ($finalCount)"

if (-not $SkipAlertCheck -and $hitQueue -eq 'stock.order-created.dlq') {
    Write-Step "Waiting for Prometheus alert ShopNGoDlqBacklogNonZero to fire (rule has 2m 'for')"
    $alert = Wait-PrometheusAlertFiring -AlertName 'ShopNGoDlqBacklogNonZero' -PrometheusBaseUrl $PrometheusBase -TimeoutSeconds $TimeoutSeconds
    Write-Info "Alert firing: $($alert.labels.alertname) queue=$($alert.labels.queue)"
}
elseif (-not $SkipAlertCheck) {
    Write-Warn "Skipping DLQ alert wait because message landed on '$hitQueue' (MassTransit fault queue)."
}

Write-Ok "Poison message fault-queue drill passed."
