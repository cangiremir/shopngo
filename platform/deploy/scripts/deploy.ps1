[CmdletBinding()]
param(
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment = 'prod',
    [string]$EnvFilePath = '',
    [switch]$PullImages
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
    throw "Environment file not found: $EnvFilePath. Copy the matching *.env.example and fill required values first."
}

Write-Host "Validating docker compose config"
docker compose --env-file $EnvFilePath -f $baseCompose -f $prodOverride config | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "docker compose config validation failed."
}

if ($PullImages) {
    Write-Host "Pulling images"
    docker compose --env-file $EnvFilePath -f $baseCompose -f $prodOverride pull
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose pull failed."
    }
}

Write-Host "Starting services"
docker compose --env-file $EnvFilePath -f $baseCompose -f $prodOverride up -d --remove-orphans
if ($LASTEXITCODE -ne 0) {
    throw "docker compose up failed."
}

Write-Host "Deployment complete. Current status:"
docker compose --env-file $EnvFilePath -f $baseCompose -f $prodOverride ps
