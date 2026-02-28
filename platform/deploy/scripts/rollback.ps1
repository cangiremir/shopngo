[CmdletBinding()]
param(
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment = 'prod',
    [string]$EnvFilePath = '',
    [string]$OrderServiceImage = '',
    [string]$StockServiceImage = '',
    [string]$NotificationServiceImage = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$baseCompose = Join-Path $repoRoot 'docker-compose.yml'
$prodOverride = Join-Path $repoRoot 'platform/deploy/compose/docker-compose.prod.yml'

if ([string]::IsNullOrWhiteSpace($EnvFilePath)) {
    $EnvFilePath = Join-Path $repoRoot "platform/deploy/environments/$Environment.env"
}

if (-not (Test-Path $EnvFilePath)) {
    throw "Environment file not found: $EnvFilePath."
}

if (-not [string]::IsNullOrWhiteSpace($OrderServiceImage)) {
    $env:ORDER_SERVICE_IMAGE = $OrderServiceImage
}

if (-not [string]::IsNullOrWhiteSpace($StockServiceImage)) {
    $env:STOCK_SERVICE_IMAGE = $StockServiceImage
}

if (-not [string]::IsNullOrWhiteSpace($NotificationServiceImage)) {
    $env:NOTIFICATION_SERVICE_IMAGE = $NotificationServiceImage
}

Write-Host "Validating docker compose config"
docker compose --env-file $EnvFilePath -f $baseCompose -f $prodOverride config | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "docker compose config validation failed."
}

Write-Host "Applying rollback image set"
docker compose --env-file $EnvFilePath -f $baseCompose -f $prodOverride up -d order-service stock-service notification-service
if ($LASTEXITCODE -ne 0) {
    throw "Rollback deployment failed."
}

Write-Host "Rollback applied. Current status:"
docker compose --env-file $EnvFilePath -f $baseCompose -f $prodOverride ps
