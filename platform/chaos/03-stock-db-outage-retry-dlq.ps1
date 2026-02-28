[CmdletBinding()]
param(
    [string]$OrderBaseUrl = 'http://localhost:8081',
    [string]$StockBaseUrl = 'http://localhost:8082',
    [string]$RabbitApiBase = 'http://localhost:15672/api',
    [string]$RabbitUser = 'guest',
    [string]$RabbitPassword = 'guest',
    [int]$TimeoutSeconds = 300,
    [bool]$ResetFaultQueues = $true,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Common.ps1"
Write-Section "Stock DB Outage Retry/DLQ Drill"

$productId = [Guid]::NewGuid()
$email = "stockdb-outage+$([Guid]::NewGuid().ToString('N'))@example.com"
$queueCandidates = @(
    'stock.order-created.dlq',
    'stock.order-created_error'
)

if ($DryRun) {
    Write-Step "Dry run: would seed stock, stop stock-db, create order, wait for fault queue increment, restart stock-db, replay message, and verify finalization."
    if ($ResetFaultQueues) {
        Write-Info "Dry run: fault queue reset is enabled."
    }
    Invoke-Compose -Args @('stop', 'stock-db') -DryRun
    Invoke-Compose -Args @('start', 'stock-db') -DryRun
    Write-Ok "Dry run completed."
    return
}

Write-Step "Seeding stock before DB outage"
Seed-Stock -StockBaseUrl $StockBaseUrl -ProductId $productId -Quantity 10

Write-Step "Capturing baseline fault queue counts"
$baselines = @{}
foreach ($queue in $queueCandidates) {
    try {
        $count = Get-RabbitQueueCount -QueueName $queue -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
        $baselines[$queue] = $count
        Write-Info ("Baseline {0}: {1}" -f $queue, $count)
    }
    catch {
        $baselines[$queue] = $null
    }
}

if ($ResetFaultQueues) {
    Write-Step "Resetting fault queues for deterministic drill run"
    foreach ($queue in $queueCandidates) {
        try {
            Purge-RabbitQueue -QueueName $queue -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
            Wait-Until -TimeoutSeconds 20 -Description "queue '$queue' purge to reach zero" -Condition {
                try {
                    $current = Get-RabbitQueueCount -QueueName $queue -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
                    return ($current -eq 0)
                }
                catch {
                    return $false
                }
            }
            Write-Info "Purged queue: $queue"
        }
        catch {
            Write-Warn "Unable to purge queue '$queue': $($_.Exception.Message)"
        }
    }

    Write-Step "Re-capturing baseline fault queue counts after reset"
    foreach ($queue in $queueCandidates) {
        try {
            $count = Get-RabbitQueueCount -QueueName $queue -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
            $baselines[$queue] = $count
            Write-Info ("Baseline {0}: {1}" -f $queue, $count)
        }
        catch {
            $baselines[$queue] = $null
        }
    }
}

Write-Step "Stopping stock-db"
Invoke-Compose -Args @('stop', 'stock-db')

Write-Step "Creating order while stock DB is down"
$created = Create-Order -OrderBaseUrl $OrderBaseUrl -CustomerEmail $email -ProductId $productId -Quantity 1
$orderId = [Guid]$created.id
Write-Info "Order created: $orderId"

Write-Step "Verifying order remains PendingStock while stock DB is down"
$pending = Wait-OrderStatus -OrderBaseUrl $OrderBaseUrl -OrderId $orderId -ExpectedStatus 'PendingStock' -TimeoutSeconds 30
Assert-Equal -Expected 'PendingStock' -Actual ([string]$pending.Status) -Message "Order did not remain PendingStock."

Write-Step "Waiting for message to reach a fault queue"
$hitQueue = $null
$hitCount = $null
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
                $script:hitCount = $current
                return $true
            }
        }
        catch {
            continue
        }
    }

    return $false
}
Write-Info "Fault queue incremented: $hitQueue ($hitCount)"

Write-Step "Starting stock-db"
Invoke-Compose -Args @('start', 'stock-db')

Write-Step "Waiting for stock-service readiness"
Wait-Until -TimeoutSeconds 90 -Description "stock-service readiness" -Condition {
    try {
        $resp = Invoke-WebRequest -Method GET -Uri "$($StockBaseUrl.TrimEnd('/'))/health/ready" -TimeoutSec 10 -ErrorAction Stop
        return ($resp.StatusCode -eq 200)
    }
    catch {
        return $false
    }
}

Write-Step "Replaying one message from $hitQueue"
& "$PSScriptRoot/05-dlq-replay.ps1" -QueueName $hitQueue -RabbitApiBase $RabbitApiBase -RabbitUser $RabbitUser -RabbitPassword $RabbitPassword | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "DLQ replay script failed."
}

Write-Step "Waiting for order to finalize after stock DB recovery and replay"
$finalOrder = Wait-OrderFinalState -OrderBaseUrl $OrderBaseUrl -OrderId $orderId -TimeoutSeconds $TimeoutSeconds
Assert-True -Condition (@('Confirmed', 'Rejected') -contains [string]$finalOrder.Status) -Message "Order did not reach final state after stock DB recovery."
if ([string]$finalOrder.Status -eq 'Confirmed') {
    Write-Ok "Final status: $($finalOrder.Status)"
}
else {
    Write-Warn "Final status: $($finalOrder.Status)"
}

Write-Ok "Stock DB outage / retry / DLQ / replay drill passed."
