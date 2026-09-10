# Seed full demo dataset via API (farm + IoT + harvest + frozen)
# Chạy khi DB trống hoặc muốn bổ sung IoT/alert mà chưa deploy seeder C#.
# Usage:
#   powershell -File .\seed-demo-full.ps1
#   $env:CRABSENSE_API='http://103.69.96.143:5080'; powershell -File .\seed-demo-full.ps1

$ErrorActionPreference = 'Stop'
$base = if ($env:CRABSENSE_API) { $env:CRABSENSE_API.TrimEnd('/') } else { 'http://103.69.96.143:5080' }

function Login {
  $r = Invoke-RestMethod "$base/api/auth/login" -Method Post -ContentType 'application/json' -Body '{"username":"owner","password":"Owner@123"}'
  return @{ Authorization = "Bearer $($r.data.accessToken)"; 'Content-Type' = 'application/json' }
}

function Get-List($path, $h) {
  $p = Invoke-RestMethod "$base$path" -Headers $h
  $d = $p.data
  if ($d -is [array]) { return @($d) }
  if ($d.items) { return @($d.items) }
  if ($d) { return @($d) }
  return @()
}

function Post($path, $body, $h) {
  $json = if ($body -is [string]) { $body } else { $body | ConvertTo-Json -Depth 8 -Compress }
  return Invoke-RestMethod "$base$path" -Method Post -Headers $h -Body $json
}

Write-Host "API: $base"
$h = Login

# Skip farm hierarchy if demo area already exists (C# seeder or prior run)
$areas = Get-List '/api/farming-areas' $h
$existing = @($areas) | Where-Object { $_.name -like 'Khu Demo*' } | Select-Object -First 1
if ($existing) {
  Write-Host "Demo area exists: $($existing.name) — skip farm seed, run IoT extras only."
} else {
  Write-Host "Seeding farm hierarchy..."
  $areaA = Post '/api/farming-areas' @{ name = 'Khu Demo RAS-A'; description = 'Khu nuôi demo — API seed' } $h
  $areaB = Post '/api/farming-areas' @{ name = 'Khu Demo RAS-B'; description = 'Khu phụ demo' } $h
  $areaAId = $areaA.data.id
  $areaBId = $areaB.data.id

  $rowA1 = Post '/api/farming-rows' @{ farmingAreaId = $areaAId; name = 'Dãy A1'; capacity = 5 } $h
  $rowA2 = Post '/api/farming-rows' @{ farmingAreaId = $areaAId; name = 'Dãy A2'; capacity = 3 } $h
  $rowB1 = Post '/api/farming-rows' @{ farmingAreaId = $areaBId; name = 'Dãy B1'; capacity = 2 } $h

  $lot1 = Post '/api/crab-lots' @{ lotCode = 'LOT-2026-001'; importDate = (Get-Date).AddDays(-45).ToUniversalTime().ToString('o'); supplierName = 'Cà Mau' } $h
  $lot2 = Post '/api/crab-lots' @{ lotCode = 'LOT-2026-002'; importDate = (Get-Date).AddDays(-20).ToUniversalTime().ToString('o'); supplierName = 'Bạc Liêu' } $h
  $batch = Post '/api/crop-batches' @{ batchCode = 'BATCH-2026-Q3'; startDate = (Get-Date).AddDays(-40).ToUniversalTime().ToString('o'); notes = 'Vụ demo' } $h

  $boxes = Get-List '/api/boxes' $h | Where-Object { $_.areaName -like 'Khu Demo*' -or $_.rowName -like 'Dãy*' }
  $empty = @($boxes) | Where-Object { -not $_.isOccupied } | Select-Object -First 6
  $tags = @('CRAB-A01','CRAB-A02','CRAB-A03','CRAB-A04','CRAB-A05','CRAB-B01')
  $stages = @('hard-shell','molting','pre-molt','post-molt','hard-shell','hard-shell')
  for ($i = 0; $i -lt [Math]::Min($empty.Count, $tags.Count); $i++) {
    $b = $empty[$i]
    Post '/api/crabs' @{
      boxId = $b.id; crabLotId = $(if ($i -lt 3) { $lot1.data.id } else { $lot2.data.id })
      cropBatchId = $batch.data.id; tag = $tags[$i]; weightGram = 200 + ($i * 5)
      moltingStage = $stages[$i]
    } $h | Out-Null
  }
  Write-Host "Farm seed done."
}

# IoT + alerts (idempotent-ish — may duplicate if run many times)
& "$PSScriptRoot\seed-demo-iot.ps1"

Write-Host "Full seed finished. Login UI owner/Owner@123 and open tabs 1-4, 7-10, 15-16."
