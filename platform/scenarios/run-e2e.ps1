[CmdletBinding()]
param(
    [string]$OrderBaseUrl = "http://localhost:8081",
    [string]$StockBaseUrl = "http://localhost:8082",
    [string]$NotificationBaseUrl = "http://localhost:8083",
    [string]$CustomerEmail = "customer@example.com",
    [ValidateSet("email", "sms")]
    [string]$NotificationChannel = "email",
    [string]$CustomerPhone = "",
    [int]$SeedQty = 20,
    [int]$OrderQty = 2,
    [int]$PollIntervalSeconds = 1,
    [int]$TimeoutSeconds = 45,
    [ValidateSet("Any", "Confirmed", "Rejected")]
    [string]$ExpectedStatus = "Any"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Section([string]$Message)
{
    Write-Host ""
    Write-Host "🧭 === $Message ===" -ForegroundColor Cyan
}

function Write-Step([string]$Message)
{
    Write-Host "🔹 $Message" -ForegroundColor DarkCyan
}

function Write-Info([string]$Message)
{
    Write-Host "ℹ️  $Message" -ForegroundColor Gray
}

function Write-Ok([string]$Message)
{
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Fail([string]$Message)
{
    Write-Host "❌ $Message" -ForegroundColor Red
}

try
{
    Write-Section "ShopNGo E2E Scenario"
    $productId = [Guid]::NewGuid()
    Write-Info "ProductId: $productId"

    $seedRequest = @{
        items = @(
            @{
                productId = $productId
                quantity  = $SeedQty
            }
        )
    } | ConvertTo-Json -Depth 5

    Write-Step "Seeding stock"
    Invoke-RestMethod -Method Post -Uri "$StockBaseUrl/stock/seed" -ContentType "application/json" -Body $seedRequest | Out-Null
    Write-Ok "Stock seeded"

    if ($NotificationChannel -eq "sms" -and [string]::IsNullOrWhiteSpace($CustomerPhone))
    {
        throw "CustomerPhone must be set when NotificationChannel is 'sms'."
    }

    $orderPayload = @{
        customerEmail       = $CustomerEmail
        notificationChannel = $NotificationChannel
        items               = @(
            @{
                productId = $productId
                quantity  = $OrderQty
            }
        )
    }

    if ($NotificationChannel -eq "sms")
    {
        $orderPayload.customerPhone = $CustomerPhone
    }

    $orderRequest = $orderPayload | ConvertTo-Json -Depth 5

    Write-Step "Creating order"
    $order = Invoke-RestMethod -Method Post -Uri "$OrderBaseUrl/orders" -ContentType "application/json" -Body $orderRequest

    if (-not $order.id)
    {
        throw "Failed to parse order id from order response."
    }

    $orderId = [string]$order.id
    Write-Info "OrderId: $orderId"

    $orderDeadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $terminalStatus = $null
    $lastOrder = $null

    Write-Step "Waiting for terminal order status"
    while ((Get-Date) -lt $orderDeadline)
    {
        $lastOrder = Invoke-RestMethod -Method Get -Uri "$OrderBaseUrl/orders/$orderId"
        $status = [string]$lastOrder.status

        if ($status -eq "Confirmed" -or $status -eq "Rejected")
        {
            $terminalStatus = $status
            break
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    if (-not $terminalStatus)
    {
        $lastOrderJson = if ($lastOrder) { $lastOrder | ConvertTo-Json -Depth 10 -Compress } else { "<none>" }
        throw "Order did not reach terminal state within ${TimeoutSeconds}s. Last response: $lastOrderJson"
    }

    if ($ExpectedStatus -ne "Any" -and $terminalStatus -ne $ExpectedStatus)
    {
        throw "Unexpected terminal status. Expected '$ExpectedStatus' but got '$terminalStatus'."
    }

    if ($terminalStatus -eq "Confirmed")
    {
        Write-Ok "Order terminal status: $terminalStatus"
    }
    else
    {
        Write-Host "⚠️  Order terminal status: $terminalStatus" -ForegroundColor Yellow
    }

    Write-Step "Waiting for notification log"
    $notificationSeen = $false
    $notificationDeadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $notificationDeadline)
    {
        $notificationsResponse = Invoke-RestMethod -Method Get -Uri "$NotificationBaseUrl/notifications"
        $notifications = if ($null -eq $notificationsResponse)
        {
            @()
        }
        elseif ($notificationsResponse -is [System.Array])
        {
            $notificationsResponse
        }
        else
        {
            @($notificationsResponse)
        }

        foreach ($row in $notifications)
        {
            $rowOrderId = $null
            if ($row.PSObject.Properties.Match("orderId").Count -gt 0)
            {
                $rowOrderId = [string]$row.orderId
            }
            elseif ($row.PSObject.Properties.Match("OrderId").Count -gt 0)
            {
                $rowOrderId = [string]$row.OrderId
            }

            if ($rowOrderId -eq $orderId)
            {
                $notificationSeen = $true
                break
            }
        }

        if ($notificationSeen)
        {
            break
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    if (-not $notificationSeen)
    {
        throw "Notification log not found for order $orderId within ${TimeoutSeconds}s."
    }

    Write-Ok "Notification log found"
    Write-Section "E2E Result"
    Write-Host "🎉 E2E scenario passed." -ForegroundColor Green
    Write-Host ("- ProductId: {0}" -f $productId) -ForegroundColor Gray
    Write-Host ("- OrderId:   {0}" -f $orderId) -ForegroundColor Gray
    if ($terminalStatus -eq "Confirmed")
    {
        Write-Host ("- Status:    {0}" -f $terminalStatus) -ForegroundColor Green
    }
    else
    {
        Write-Host ("- Status:    {0}" -f $terminalStatus) -ForegroundColor Yellow
    }
}
catch
{
    Write-Section "E2E Result"
    Write-Fail $_.Exception.Message
    exit 1
}
