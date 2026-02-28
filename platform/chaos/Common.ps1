$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Section {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host ""
    Write-Host "🧭 === $Message ===" -ForegroundColor Cyan
}

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "🔹 $Message" -ForegroundColor DarkCyan
}

function Write-Info {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "ℹ️  $Message" -ForegroundColor Gray
}

function Write-Ok {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Warn {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "⚠️  $Message" -ForegroundColor Yellow
}

function Write-Fail {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "❌ $Message" -ForegroundColor Red
}

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)]$Actual,
        [Parameter(Mandatory)][string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message (expected=$Expected actual=$Actual)"
    }
}

function Wait-Until {
    param(
        [Parameter(Mandatory)][scriptblock]$Condition,
        [int]$TimeoutSeconds = 60,
        [int]$PollMilliseconds = 1000,
        [string]$Description = "condition"
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $rawResult = & $Condition
        $result = if ($rawResult -is [System.Array]) { $rawResult[-1] } else { $rawResult }

        if ([bool]$result) {
            return
        }

        Start-Sleep -Milliseconds $PollMilliseconds
    }

    throw "Timed out waiting for $Description after ${TimeoutSeconds}s."
}

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Uri,
        $Body = $null,
        [hashtable]$Headers = @{},
        [int]$TimeoutSeconds = 30
    )

    $params = @{
        Method      = $Method
        Uri         = $Uri
        Headers     = $Headers
        TimeoutSec  = $TimeoutSeconds
        ErrorAction = 'Stop'
    }

    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
    }

    return Invoke-RestMethod @params
}

function New-BasicAuthHeader {
    param(
        [Parameter(Mandatory)][string]$UserName,
        [Parameter(Mandatory)][string]$Password
    )

    $raw = [Text.Encoding]::ASCII.GetBytes("${UserName}:$Password")
    $token = [Convert]::ToBase64String($raw)
    return @{ Authorization = "Basic $token" }
}

function ConvertTo-RabbitVhostPath {
    param([string]$VirtualHost = '/')
    if ($VirtualHost -eq '/') { return '%2F' }
    return [Uri]::EscapeDataString($VirtualHost)
}

function Invoke-RabbitApi {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        $Body = $null,
        [string]$BaseUri = 'http://localhost:15672/api',
        [string]$UserName = 'guest',
        [string]$Password = 'guest',
        [int]$TimeoutSeconds = 30
    )

    $headers = New-BasicAuthHeader -UserName $UserName -Password $Password
    $trimmedBase = $BaseUri.TrimEnd('/')
    $trimmedPath = if ($Path.StartsWith('/')) { $Path } else { "/$Path" }

    return Invoke-JsonRequest -Method $Method -Uri "$trimmedBase$trimmedPath" -Body $Body -Headers $headers -TimeoutSeconds $TimeoutSeconds
}

function Get-RabbitOverview {
    param(
        [string]$BaseUri = 'http://localhost:15672/api',
        [string]$UserName = 'guest',
        [string]$Password = 'guest'
    )

    Invoke-RabbitApi -Method GET -Path '/overview' -BaseUri $BaseUri -UserName $UserName -Password $Password
}

function Get-RabbitQueue {
    param(
        [Parameter(Mandatory)][string]$QueueName,
        [string]$VirtualHost = '/',
        [string]$BaseUri = 'http://localhost:15672/api',
        [string]$UserName = 'guest',
        [string]$Password = 'guest'
    )

    $vhost = ConvertTo-RabbitVhostPath -VirtualHost $VirtualHost
    $encodedQueue = [Uri]::EscapeDataString($QueueName)
    Invoke-RabbitApi -Method GET -Path "/queues/$vhost/$encodedQueue" -BaseUri $BaseUri -UserName $UserName -Password $Password
}

function Get-RabbitQueueCount {
    param(
        [Parameter(Mandatory)][string]$QueueName,
        [string]$VirtualHost = '/',
        [string]$BaseUri = 'http://localhost:15672/api',
        [string]$UserName = 'guest',
        [string]$Password = 'guest'
    )

    $queue = Get-RabbitQueue -QueueName $QueueName -VirtualHost $VirtualHost -BaseUri $BaseUri -UserName $UserName -Password $Password
    return [int]$queue.messages
}

function Wait-RabbitQueueCountAtLeast {
    param(
        [Parameter(Mandatory)][string]$QueueName,
        [Parameter(Mandatory)][int]$ExpectedMinimum,
        [int]$TimeoutSeconds = 120,
        [string]$VirtualHost = '/',
        [string]$BaseUri = 'http://localhost:15672/api',
        [string]$UserName = 'guest',
        [string]$Password = 'guest'
    )

    Wait-Until -TimeoutSeconds $TimeoutSeconds -Description "queue '$QueueName' count >= $ExpectedMinimum" -Condition {
        try {
            $current = Get-RabbitQueueCount -QueueName $QueueName -VirtualHost $VirtualHost -BaseUri $BaseUri -UserName $UserName -Password $Password
            return ($current -ge $ExpectedMinimum)
        }
        catch {
            return $false
        }
    }

    return (Get-RabbitQueueCount -QueueName $QueueName -VirtualHost $VirtualHost -BaseUri $BaseUri -UserName $UserName -Password $Password)
}

function Publish-RabbitMessage {
    param(
        [Parameter(Mandatory)][string]$Exchange,
        [Parameter(Mandatory)][string]$RoutingKey,
        [Parameter(Mandatory)][string]$Payload,
        [string]$PayloadEncoding = 'string',
        [string]$MessageId = ([Guid]::NewGuid().ToString('N')),
        [string]$CorrelationId = $MessageId,
        [hashtable]$Headers = @{},
        [string]$ContentType = 'application/json',
        [string]$VirtualHost = '/',
        [string]$BaseUri = 'http://localhost:15672/api',
        [string]$UserName = 'guest',
        [string]$Password = 'guest'
    )

    $vhost = ConvertTo-RabbitVhostPath -VirtualHost $VirtualHost
    $encodedExchange = [Uri]::EscapeDataString($Exchange)

    $body = @{
        properties = @{
            delivery_mode   = 2
            content_type    = $ContentType
            message_id      = $MessageId
            correlation_id  = $CorrelationId
            headers         = $Headers
        }
        routing_key      = $RoutingKey
        payload          = $Payload
        payload_encoding = $PayloadEncoding
    }

    return Invoke-RabbitApi -Method POST -Path "/exchanges/$vhost/$encodedExchange/publish" -Body $body -BaseUri $BaseUri -UserName $UserName -Password $Password
}

function Get-RabbitQueueMessages {
    param(
        [Parameter(Mandatory)][string]$QueueName,
        [int]$Count = 1,
        [ValidateSet('ack_requeue_true', 'ack_requeue_false', 'reject_requeue_true', 'reject_requeue_false')]
        [string]$AckMode = 'ack_requeue_true',
        [string]$VirtualHost = '/',
        [string]$BaseUri = 'http://localhost:15672/api',
        [string]$UserName = 'guest',
        [string]$Password = 'guest'
    )

    $vhost = ConvertTo-RabbitVhostPath -VirtualHost $VirtualHost
    $encodedQueue = [Uri]::EscapeDataString($QueueName)
    $body = @{
        count    = $Count
        ackmode  = $AckMode
        encoding = 'auto'
        truncate = 500000
    }

    return Invoke-RabbitApi -Method POST -Path "/queues/$vhost/$encodedQueue/get" -Body $body -BaseUri $BaseUri -UserName $UserName -Password $Password
}

function Purge-RabbitQueue {
    param(
        [Parameter(Mandatory)][string]$QueueName,
        [string]$VirtualHost = '/',
        [string]$BaseUri = 'http://localhost:15672/api',
        [string]$UserName = 'guest',
        [string]$Password = 'guest'
    )

    $vhost = ConvertTo-RabbitVhostPath -VirtualHost $VirtualHost
    $encodedQueue = [Uri]::EscapeDataString($QueueName)
    Invoke-RabbitApi -Method DELETE -Path "/queues/$vhost/$encodedQueue/contents" -BaseUri $BaseUri -UserName $UserName -Password $Password | Out-Null
}

function Invoke-ApiGet {
    param([Parameter(Mandatory)][string]$Uri)
    Invoke-RestMethod -Method GET -Uri $Uri -TimeoutSec 30 -ErrorAction Stop
}

function Invoke-ApiPost {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)]$Body
    )

    Invoke-RestMethod -Method POST -Uri $Uri -TimeoutSec 30 -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 20 -Compress) -ErrorAction Stop
}

function Seed-Stock {
    param(
        [Parameter(Mandatory)][string]$StockBaseUrl,
        [Parameter(Mandatory)][Guid]$ProductId,
        [Parameter(Mandatory)][int]$Quantity
    )

    Invoke-ApiPost -Uri "$($StockBaseUrl.TrimEnd('/'))/stock/seed" -Body @{
        items = @(@{
            productId = $ProductId
            quantity  = $Quantity
        })
    } | Out-Null
}

function Create-Order {
    param(
        [Parameter(Mandatory)][string]$OrderBaseUrl,
        [Parameter(Mandatory)][string]$CustomerEmail,
        [Parameter(Mandatory)][Guid]$ProductId,
        [Parameter(Mandatory)][int]$Quantity,
        [string]$CorrelationId = ([Guid]::NewGuid().ToString('N'))
    )

    $uri = "$($OrderBaseUrl.TrimEnd('/'))/orders"
    $body = @{
        customerEmail = $CustomerEmail
        items = @(@{
            productId = $ProductId
            quantity  = $Quantity
        })
    } | ConvertTo-Json -Depth 10 -Compress

    $response = Invoke-WebRequest -Method POST -Uri $uri -ContentType 'application/json' -Body $body -Headers @{ 'x-correlation-id' = $CorrelationId } -TimeoutSec 30 -ErrorAction Stop
    return ($response.Content | ConvertFrom-Json)
}

function Get-Order {
    param(
        [Parameter(Mandatory)][string]$OrderBaseUrl,
        [Parameter(Mandatory)][Guid]$OrderId
    )

    return Invoke-ApiGet -Uri "$($OrderBaseUrl.TrimEnd('/'))/orders/$OrderId"
}

function Wait-OrderStatus {
    param(
        [Parameter(Mandatory)][string]$OrderBaseUrl,
        [Parameter(Mandatory)][Guid]$OrderId,
        [Parameter(Mandatory)][string]$ExpectedStatus,
        [int]$TimeoutSeconds = 120
    )

    Wait-Until -TimeoutSeconds $TimeoutSeconds -Description "order $OrderId status = $ExpectedStatus" -Condition {
        try {
            $current = Get-Order -OrderBaseUrl $OrderBaseUrl -OrderId $OrderId
            return ($null -ne $current -and $current.Status -eq $ExpectedStatus)
        }
        catch {
            return $false
        }
    }

    return (Get-Order -OrderBaseUrl $OrderBaseUrl -OrderId $OrderId)
}

function Wait-OrderFinalState {
    param(
        [Parameter(Mandatory)][string]$OrderBaseUrl,
        [Parameter(Mandatory)][Guid]$OrderId,
        [int]$TimeoutSeconds = 120
    )

    Wait-Until -TimeoutSeconds $TimeoutSeconds -Description "order $OrderId final state" -Condition {
        try {
            $current = Get-Order -OrderBaseUrl $OrderBaseUrl -OrderId $OrderId
            return ($null -ne $current -and @('Confirmed', 'Rejected') -contains [string]$current.Status)
        }
        catch {
            return $false
        }
    }

    return (Get-Order -OrderBaseUrl $OrderBaseUrl -OrderId $OrderId)
}

function Get-Notifications {
    param([Parameter(Mandatory)][string]$NotificationBaseUrl)
    return @(Invoke-ApiGet -Uri "$($NotificationBaseUrl.TrimEnd('/'))/notifications")
}

function Get-PrometheusAlerts {
    param([string]$PrometheusBaseUrl = 'http://localhost:9090')
    $result = Invoke-ApiGet -Uri "$($PrometheusBaseUrl.TrimEnd('/'))/api/v1/alerts"
    return @($result.data.alerts)
}

function Wait-PrometheusAlertFiring {
    param(
        [Parameter(Mandatory)][string]$AlertName,
        [string]$PrometheusBaseUrl = 'http://localhost:9090',
        [int]$TimeoutSeconds = 240
    )

    Wait-Until -TimeoutSeconds $TimeoutSeconds -Description "Prometheus alert '$AlertName' firing" -PollMilliseconds 5000 -Condition {
        try {
            $alerts = Get-PrometheusAlerts -PrometheusBaseUrl $PrometheusBaseUrl
            $match = $alerts | Where-Object {
                $_.labels.alertname -eq $AlertName -and $_.state -eq 'firing'
            } | Select-Object -First 1
            return ($null -ne $match)
        }
        catch {
            return $false
        }
    }

    return (Get-PrometheusAlerts -PrometheusBaseUrl $PrometheusBaseUrl | Where-Object {
            $_.labels.alertname -eq $AlertName -and $_.state -eq 'firing'
        } | Select-Object -First 1)
}

function Get-AlertmanagerAlerts {
    param([string]$AlertmanagerBaseUrl = 'http://localhost:9093')
    return @(Invoke-ApiGet -Uri "$($AlertmanagerBaseUrl.TrimEnd('/'))/api/v2/alerts")
}

function Invoke-Compose {
    param(
        [Parameter(Mandatory)][string[]]$Args,
        [switch]$DryRun
    )

    if ($DryRun) {
        Write-Info ("[DryRun] docker compose " + ($Args -join ' '))
        return
    }

    & docker compose @Args
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose command failed: $($Args -join ' ')"
    }
}

function Invoke-ComposePsqlScalar {
    param(
        [Parameter(Mandatory)][string]$Service,
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Sql
    )

    $output = & docker compose exec -T $Service psql -U postgres -d $Database -t -A -c $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "psql query failed for service '$Service'."
    }

    return ($output | Out-String).Trim()
}
