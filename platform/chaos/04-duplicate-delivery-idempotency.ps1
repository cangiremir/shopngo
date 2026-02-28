[CmdletBinding()]
param(
    [string]$NotificationBaseUrl = 'http://localhost:8083',
    [string]$RabbitApiBase = 'http://localhost:15672/api',
    [string]$RabbitUser = 'guest',
    [string]$RabbitPassword = 'guest',
    [int]$TimeoutSeconds = 60,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Common.ps1"
Write-Section "Duplicate Delivery Idempotency Drill"

$orderId = [Guid]::NewGuid()
$messageId = [Guid]::NewGuid().ToString('N')
$email = "dupe+$([Guid]::NewGuid().ToString('N'))@example.com"
$payload = @{
    orderId = $orderId
    customerEmail = $email
    confirmedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    notificationChannel = 'email'
    notificationTarget = $email
} | ConvertTo-Json -Compress

if ($DryRun) {
    Write-Step "Dry run: would publish duplicate order.confirmed messages with messageId=$messageId and verify only one notification row."
    Write-Ok "Dry run completed."
    return
}

Write-Step "Publishing duplicate order.confirmed messages with same messageId"
$r1 = Publish-RabbitMessage -Exchange 'ecommerce.events' -RoutingKey 'order.confirmed' -Payload $payload -MessageId $messageId -CorrelationId $messageId -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
$r2 = Publish-RabbitMessage -Exchange 'ecommerce.events' -RoutingKey 'order.confirmed' -Payload $payload -MessageId $messageId -CorrelationId $messageId -BaseUri $RabbitApiBase -UserName $RabbitUser -Password $RabbitPassword
Assert-True -Condition ([bool]$r1.routed -and [bool]$r2.routed) -Message "Duplicate publish did not route."

Write-Step "Waiting for a single notification row"
$matched = @()
Wait-Until -TimeoutSeconds $TimeoutSeconds -Description "single notification record for duplicate message" -Condition {
    try {
        $rows = Get-Notifications -NotificationBaseUrl $NotificationBaseUrl
        $matched = @($rows | Where-Object { [string]$_.OrderId -eq $orderId.ToString() })
        return ($matched.Count -eq 1)
    }
    catch {
        return $false
    }
}

$rows = Get-Notifications -NotificationBaseUrl $NotificationBaseUrl
$matched = @($rows | Where-Object { [string]$_.OrderId -eq $orderId.ToString() })
Assert-Equal -Expected 1 -Actual $matched.Count -Message "Expected exactly one notification log row for duplicate delivery."
Write-Info "Notification row id: $($matched[0].Id)"

Write-Ok "Duplicate delivery idempotency drill passed."
