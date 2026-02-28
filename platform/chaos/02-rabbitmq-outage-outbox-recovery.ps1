[CmdletBinding()]
param(
    [string]$OrderBaseUrl = 'http://localhost:8081',
    [string]$StockBaseUrl = 'http://localhost:8082',
    [string]$RabbitApiBase = 'http://localhost:15672/api',
    [string]$RabbitUser = 'guest',
    [string]$RabbitPassword = 'guest',
    [int]$TimeoutSeconds = 180,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Common.ps1"
Write-Section "RabbitMQ Outage and Outbox Recovery Drill"

$productId = [Guid]::NewGuid()
$seedQty = 5
$orderQty = 1
$email = "outage+$([Guid]::NewGuid().ToString('N'))@example.com"

if ($DryRun) {
    Write-Step "Dry run: would seed stock, stop rabbitmq, create order, verify PendingStock, restart rabbitmq, and wait for order finalization."
    Invoke-Compose -Args @('stop', 'rabbitmq') -DryRun
    Invoke-Compose -Args @('start', 'rabbitmq') -DryRun
    Write-Ok "Dry run completed."
    return
}

Write-Step "Seeding stock"
Seed-Stock -StockBaseUrl $StockBaseUrl -ProductId $productId -Quantity $seedQty

Write-Step "Stopping rabbitmq"
Invoke-Compose -Args @('stop', 'rabbitmq')

Write-Step "Creating order while broker is down (outbox persistence path)"
$created = Create-Order -OrderBaseUrl $OrderBaseUrl -CustomerEmail $email -ProductId $productId -Quantity $orderQty
Write-Info "Order created: $($created.id)"

Write-Step "Verifying order remains PendingStock while RabbitMQ is down"
$pending = Wait-OrderStatus -OrderBaseUrl $OrderBaseUrl -OrderId ([Guid]$created.id) -ExpectedStatus 'PendingStock' -TimeoutSeconds 30
Assert-Equal -Expected 'PendingStock' -Actual ([string]$pending.Status) -Message "Order did not remain PendingStock during broker outage."

Write-Step "Starting rabbitmq"
Invoke-Compose -Args @('start', 'rabbitmq')

Write-Step "Waiting for RabbitMQ management API to return"
Wait-Until -TimeoutSeconds 90 -Description "RabbitMQ management API availability" -Condition {
    try {
        $null = Get-RabbitOverview -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
        return $true
    }
    catch {
        return $false
    }
}

Write-Step "Waiting for order to finalize after broker recovery"
$finalOrder = Wait-OrderFinalState -OrderBaseUrl $OrderBaseUrl -OrderId ([Guid]$created.id) -TimeoutSeconds $TimeoutSeconds
Assert-True -Condition (@('Confirmed', 'Rejected') -contains [string]$finalOrder.Status) -Message "Order did not reach final state after broker recovery."
if ([string]$finalOrder.Status -eq 'Confirmed') {
    Write-Ok "Final status: $($finalOrder.Status)"
}
else {
    Write-Warn "Final status: $($finalOrder.Status)"
}

Write-Ok "RabbitMQ outage / outbox recovery drill passed."
