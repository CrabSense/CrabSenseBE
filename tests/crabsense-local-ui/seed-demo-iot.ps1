# Seed demo data for CrabSense local UI (IoT + sample molting)
# Usage: powershell -File .\seed-demo-iot.ps1
$ErrorActionPreference = 'Continue'
$base = if ($env:CRABSENSE_API) { $env:CRABSENSE_API.TrimEnd('/') } else { 'http://localhost:5080' }

$login = Invoke-RestMethod "$base/api/auth/login" -Method Post -ContentType application/json -Body '{"username":"owner","password":"Owner@123"}'
$h = @{ Authorization = "Bearer $($login.data.accessToken)"; 'Content-Type' = 'application/json' }

function Post($path, $body) {
  try {
    $json = if ($body -is [string]) { $body } else { $body | ConvertTo-Json -Depth 8 -Compress }
    return Invoke-RestMethod "$base$path" -Method Post -Headers $h -Body $json
  } catch {
    return @{ success = $false; message = $_.ErrorDetails.Message; error = $true }
  }
}
function Put($path, $body) {
  try {
    return Invoke-RestMethod "$base$path" -Method Put -Headers $h -Body ($body | ConvertTo-Json -Depth 6 -Compress)
  } catch { return @{ error = $true } }
}

Write-Host "API $base — seeding..."

Post '/api/water-systems' @{ name = 'RAS-Demo-A'; type = 'RAS' } | Out-Null
$allWs = (Invoke-RestMethod "$base/api/water-systems" -Headers $h).data
$wsId = (@($allWs) | Where-Object { $_.name -eq 'RAS-Demo-A' } | Select-Object -First 1).id

Post '/api/devices' @{ deviceCode = 'ESP32-DEMO-A'; firmwareVersion = '1.0.0-demo' } | Out-Null
Post '/api/devices' @{ deviceCode = 'ESP32-DEMO-B'; firmwareVersion = '1.1.0-demo' } | Out-Null
$allDev = (Invoke-RestMethod "$base/api/devices" -Headers $h).data
$devA = @($allDev) | Where-Object { $_.deviceCode -eq 'ESP32-DEMO-A' } | Select-Object -First 1
$devB = @($allDev) | Where-Object { $_.deviceCode -eq 'ESP32-DEMO-B' } | Select-Object -First 1

@(
  @{ sensorCode = 'TEMP-DEMO-01'; sensorType = 'Temperature'; unit = 'C'; deviceId = $devA.id; waterSystemId = $wsId; minThreshold = 24; maxThreshold = 30 }
  @{ sensorCode = 'PH-DEMO-01'; sensorType = 'pH'; unit = 'pH'; deviceId = $devA.id; waterSystemId = $wsId; minThreshold = 7.5; maxThreshold = 8.5 }
  @{ sensorCode = 'DO-DEMO-01'; sensorType = 'DO'; unit = 'mg/L'; deviceId = $devA.id; waterSystemId = $wsId; minThreshold = 5; maxThreshold = 9 }
  @{ sensorCode = 'SAL-DEMO-01'; sensorType = 'Salinity'; unit = 'ppt'; deviceId = $devB.id; waterSystemId = $wsId; minThreshold = 15; maxThreshold = 35 }
  @{ sensorCode = 'TDS-DEMO-01'; sensorType = 'TDS'; unit = 'ppm'; deviceId = $devB.id; minThreshold = 100; maxThreshold = 2000 }
) | ForEach-Object { Post '/api/sensors' $_ | Out-Null }

@(
  @{ sensorType = 'Temperature'; minValue = 22; maxValue = 32; severity = 'Warning' }
  @{ sensorType = 'pH'; minValue = 7.0; maxValue = 8.8; severity = 'Critical' }
  @{ sensorType = 'DO'; minValue = 4.5; maxValue = 10; severity = 'Warning' }
  @{ sensorType = 'Salinity'; minValue = 10; maxValue = 40; severity = 'Info' }
) | ForEach-Object { Post '/api/alert-thresholds' $_ | Out-Null }

$now = (Get-Date).ToUniversalTime().ToString('o')
Post '/api/iot/sensor-data/batch' @{
  measurements = @(
    @{ deviceCode = 'ESP32-DEMO-A'; sensorCode = 'TEMP-DEMO-01'; value = 28.2; unit = 'C'; measuredAt = $now }
    @{ deviceCode = 'ESP32-DEMO-A'; sensorCode = 'PH-DEMO-01'; value = 8.1; unit = 'pH'; measuredAt = $now }
    @{ deviceCode = 'ESP32-DEMO-A'; sensorCode = 'DO-DEMO-01'; value = 4.2; unit = 'mg/L'; measuredAt = $now }
    @{ deviceCode = 'ESP32-DEMO-B'; sensorCode = 'SAL-DEMO-01'; value = 28; unit = 'ppt'; measuredAt = $now }
    @{ deviceCode = 'ESP32-DEMO-B'; sensorCode = 'TDS-DEMO-01'; value = 2500; unit = 'ppm'; measuredAt = $now }
  )
} | Out-Null

if ($devA.id) { Put "/api/devices/$($devA.id)/status" @{ status = 'Online'; batteryLevel = 87; rssiDbm = -55 } | Out-Null }
if ($devB.id) { Put "/api/devices/$($devB.id)/status" @{ status = 'Online'; batteryLevel = 72; rssiDbm = -62 } | Out-Null }

$crabs = (Invoke-RestMethod "$base/api/crabs" -Headers $h).data
$items = if ($crabs.items) { $crabs.items } else { $crabs }
$crab = @($items) | Where-Object { $_.isAlive -ne $false } | Select-Object -First 1
if ($crab) {
  Post "/api/crabs/$($crab.id)/moltings" @{ weightAfterGram = 205; result = 'success'; source = 'manual'; notes = 'demo seed' } | Out-Null
}

Write-Host 'Done. Refresh UI tabs: 7 Sensors, 8 Devices, 8b Live, 10 Thresholds, 13 History'
