[CmdletBinding()]
param(
    [string]$OrderBaseUrl = 'http://localhost:8081',
    [string]$StockBaseUrl = 'http://localhost:8082',
    [string]$RabbitApiBase = 'http://localhost:15672/api',
    [string]$RabbitUser = 'guest',
    [string]$RabbitPassword = 'guest',
    [int]$SeedQty = 200,
    [int]$Vus = 50,
    [int]$Iterations = 400,
    [int]$OrderQty = 1,
    [int]$FinalizeTimeoutSeconds = 300,
    [Guid]$ProductId = [Guid]::Empty,
    [string]$RunId = '',
    [switch]$UseDockerK6,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/../drills/Common.ps1"

if ($ProductId -eq [Guid]::Empty) {
    $ProductId = [Guid]::NewGuid()
}

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = [Guid]::NewGuid().ToString('N').Substring(0, 10)
}

$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactsDir = Join-Path $PSScriptRoot '.artifacts'
New-Item -ItemType Directory -Force $artifactsDir | Out-Null
$summaryPath = Join-Path $artifactsDir "k6-summary-$RunId.json"
$k6ScriptPath = Join-Path $PSScriptRoot 'k6/order-hot-product.js'

if ($DryRun) {
    Write-Step "Dry run: would seed stock, execute k6 load, then run DB and queue invariant checks."
    Write-Host "RunId=$RunId ProductId=$ProductId SeedQty=$SeedQty Vus=$Vus Iterations=$Iterations OrderQty=$OrderQty"
    return
}

Write-Step "Seeding hot product stock"
Seed-Stock -StockBaseUrl $StockBaseUrl -ProductId $ProductId -Quantity $SeedQty

$useLocalK6 = -not $UseDockerK6 -and $null -ne (Get-Command k6 -ErrorAction SilentlyContinue)

Write-Step "Running k6 load test ($Iterations iterations, $Vus VUs)"
if ($useLocalK6) {
    & k6 run "--summary-export=$summaryPath" `
        -e "ORDER_BASE_URL=$OrderBaseUrl" `
        -e "PRODUCT_ID=$ProductId" `
        -e "RUN_ID=$RunId" `
        -e "ORDER_QTY=$OrderQty" `
        -e "VUS=$Vus" `
        -e "ITERATIONS=$Iterations" `
        $k6ScriptPath
}
else {
    $dockerOrderBaseUrl = $OrderBaseUrl -replace 'localhost', 'host.docker.internal' -replace '127\.0\.0\.1', 'host.docker.internal'
    $dockerStockBaseUrl = $StockBaseUrl -replace 'localhost', 'host.docker.internal' -replace '127\.0\.0\.1', 'host.docker.internal'
    Write-Host "k6 local binary not found (or -UseDockerK6 set). Using Docker image with host.docker.internal."
    & docker run --rm `
        -v "${workspace}:/work" `
        -w /work `
        grafana/k6 run `
        "--summary-export=/work/platform/performance/.artifacts/k6-summary-$RunId.json" `
        -e "ORDER_BASE_URL=$dockerOrderBaseUrl" `
        -e "PRODUCT_ID=$ProductId" `
        -e "RUN_ID=$RunId" `
        -e "ORDER_QTY=$OrderQty" `
        -e "VUS=$Vus" `
        -e "ITERATIONS=$Iterations" `
        /work/platform/performance/k6/order-hot-product.js
}

if ($LASTEXITCODE -ne 0) {
    throw "k6 load test failed."
}

function Get-OrderCountByFilter {
    param([string]$WhereClause)
    $sql = "SELECT COUNT(*) FROM orders WHERE $WhereClause;"
    return [int](Invoke-ComposePsqlScalar -Service 'order-db' -Database 'orderdb' -Sql $sql)
}

$emailPattern = "k6+${RunId}-%@example.com"
$emailPatternSql = $emailPattern.Replace("'", "''")
$baseWhere = """CustomerEmail"" LIKE '$emailPatternSql'"

Write-Step "Waiting for created orders in this run to reach final states"
Wait-Until -TimeoutSeconds $FinalizeTimeoutSeconds -PollMilliseconds 3000 -Description "all run orders finalized" -Condition {
    try {
        $total = Get-OrderCountByFilter -WhereClause $baseWhere
        if ($total -eq 0) {
            return $false
        }

        $pending = Get-OrderCountByFilter -WhereClause "$baseWhere AND ""Status"" = 'PendingStock'"
        return ($pending -eq 0)
    }
    catch {
        return $false
    }
}

$totalOrders = Get-OrderCountByFilter -WhereClause $baseWhere
$pendingOrders = Get-OrderCountByFilter -WhereClause "$baseWhere AND ""Status"" = 'PendingStock'"
$confirmedOrders = Get-OrderCountByFilter -WhereClause "$baseWhere AND ""Status"" = 'Confirmed'"
$rejectedOrders = Get-OrderCountByFilter -WhereClause "$baseWhere AND ""Status"" = 'Rejected'"

Assert-True -Condition ($totalOrders -gt 0) -Message "No orders were created for run $RunId."
Assert-Equal -Expected 0 -Actual $pendingOrders -Message "Some orders are still PendingStock."

$confirmedIdsCsv = Invoke-ComposePsqlScalar -Service 'order-db' -Database 'orderdb' -Sql "SELECT COALESCE(string_agg(""Id""::text, ','), '') FROM orders WHERE $baseWhere AND ""Status"" = 'Confirmed';"
$stockReservationCount = 0
if (-not [string]::IsNullOrWhiteSpace($confirmedIdsCsv)) {
    $inList = ($confirmedIdsCsv.Split(',') | Where-Object { $_ } | ForEach-Object { "'$_'::uuid" }) -join ','
    $stockReservationCount = [int](Invoke-ComposePsqlScalar -Service 'stock-db' -Database 'stockdb' -Sql "SELECT COUNT(*) FROM stock_reservations WHERE ""OrderId"" IN ($inList);")
}

$availableQtyRaw = Invoke-ComposePsqlScalar -Service 'stock-db' -Database 'stockdb' -Sql "SELECT COALESCE(""AvailableQuantity"", -1) FROM inventory_items WHERE ""ProductId"" = '$ProductId'::uuid;"
$availableQty = [int]$availableQtyRaw

$expectedAvailable = $SeedQty - ($confirmedOrders * $OrderQty)
Assert-True -Condition ($availableQty -ge 0) -Message "Inventory went negative for product $ProductId (actual=$availableQty)."
Assert-Equal -Expected $expectedAvailable -Actual $availableQty -Message "Final inventory quantity mismatch."
Assert-Equal -Expected $confirmedOrders -Actual $stockReservationCount -Message "Stock reservations count must equal confirmed orders for the run."

$retryQueues = @(
    'stock.order-created.retry',
    'order.stock-reserved.retry',
    'order.stock-rejected.retry',
    'notification.order-confirmed.retry',
    'notification.order-rejected.retry'
)
$dlqQueues = @(
    'stock.order-created.dlq',
    'order.stock-reserved.dlq',
    'order.stock-rejected.dlq',
    'notification.order-confirmed.dlq',
    'notification.order-rejected.dlq'
)

$queueSummary = [ordered]@{}
foreach ($q in ($retryQueues + $dlqQueues)) {
    try {
        $queueSummary[$q] = Get-RabbitQueueCount -QueueName $q -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
    }
    catch {
        $queueSummary[$q] = -1
    }
}

$k6Summary = $null
if (Test-Path $summaryPath) {
    $k6Summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
}

$httpReqCount = $null
$httpReqFailedRate = $null
$httpReqP95 = $null
if ($null -ne $k6Summary) {
    $httpReqsMetric = $k6Summary.metrics.http_reqs
    $httpReqFailedMetric = $k6Summary.metrics.http_req_failed
    $httpReqDurationMetric = $k6Summary.metrics.http_req_duration

    if ($null -ne $httpReqsMetric) {
        if ($httpReqsMetric.PSObject.Properties.Name -contains 'values' -and $httpReqsMetric.values.count) {
            $httpReqCount = [int]$httpReqsMetric.values.count
        }
        elseif ($httpReqsMetric.PSObject.Properties.Name -contains 'count') {
            $httpReqCount = [int]$httpReqsMetric.count
        }
    }

    if ($null -ne $httpReqFailedMetric) {
        if ($httpReqFailedMetric.PSObject.Properties.Name -contains 'values' -and $httpReqFailedMetric.values.rate) {
            $httpReqFailedRate = [double]$httpReqFailedMetric.values.rate
        }
        elseif ($httpReqFailedMetric.PSObject.Properties.Name -contains 'value') {
            $httpReqFailedRate = [double]$httpReqFailedMetric.value
        }
    }

    if ($null -ne $httpReqDurationMetric) {
        if ($httpReqDurationMetric.PSObject.Properties.Name -contains 'values' -and $httpReqDurationMetric.values.'p(95)') {
            $httpReqP95 = [double]$httpReqDurationMetric.values.'p(95)'
        }
        elseif ($httpReqDurationMetric.PSObject.Properties.Name -contains 'p(95)') {
            $httpReqP95 = [double]$httpReqDurationMetric.'p(95)'
        }
    }
}

Write-Host ""
Write-Host "Phase 4 Load Validation Summary"
Write-Host "RunId: $RunId"
Write-Host "ProductId: $ProductId"
Write-Host "Orders: total=$totalOrders confirmed=$confirmedOrders rejected=$rejectedOrders pending=$pendingOrders"
Write-Host "Stock: actualAvailable=$availableQty expectedAvailable=$expectedAvailable"
Write-Host "StockReservations (for confirmed orders): $stockReservationCount"
if ($null -ne $httpReqCount) { Write-Host "k6 http_reqs.count: $httpReqCount" }
if ($null -ne $httpReqFailedRate) { Write-Host ("k6 http_req_failed.rate: {0:N4}" -f $httpReqFailedRate) }
if ($null -ne $httpReqP95) { Write-Host ("k6 http_req_duration p95: {0:N2} ms" -f $httpReqP95) }
Write-Host "Queue depths:"
foreach ($entry in $queueSummary.GetEnumerator()) {
    Write-Host ("  {0} = {1}" -f $entry.Key, $entry.Value)
}

if ($null -ne $httpReqFailedRate) {
    Assert-True -Condition ($httpReqFailedRate -lt 0.01) -Message ("k6 threshold failed: http_req_failed.rate={0}" -f $httpReqFailedRate)
}
if ($null -ne $httpReqP95) {
    Assert-True -Condition ($httpReqP95 -lt 1500) -Message ("k6 threshold failed: http_req_duration p95={0}ms" -f $httpReqP95)
}

foreach ($q in $dlqQueues) {
    if ($queueSummary[$q] -gt 0) {
        throw "Unexpected DLQ backlog during nominal load: $q=$($queueSummary[$q])"
    }
}

Write-Step "Hot-product load validation passed"
