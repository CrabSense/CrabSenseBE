# Verify luong "cua lot" theo quy uoc cua Khanh:
#   lot = tim, nguy co = do, chet/da ban = tra hop ve trong
#
# Kiem tra:
#   1. Danh dau "sap lot" (condition=premolt qua /api/operations)
#      -> /api/boxes/overview tra crabCondition=premolt   (FE to TIM)
#   2. HUONG 1 - XUAT BAN (nut "Xuat ban"): POST /api/harvest-vouchers isSoftshell=true
#      -> cua -> harvested, hop tra ve TRONG, allocation dong (kiem tra ca DB)
#   3. HUONG 2 - DE LAI NUOI TIEP: danh dau premolt roi KHONG goi API xuat
#      -> cua van trong hop, hop van co cua, cua khong bi harvested/sold
#   4. Lot thanh cong that (POST /api/crabs/{id}/moltings result=success)
#      -> crabCondition=softshell (van TIM)
#   5. Nguy co: condition=attention -> problem, condition=weak -> weak (DEU DO)
#   6. Chuyen premolt -> normal de chac chan khong dinh mau
#   7. Don sach khu ao bang cascade delete
#
# Yeu cau: BE dang chay; co psql (>= 17) de doc lai DB.
# Usage: powershell -File .\verify-molting-flow.ps1

$ErrorActionPreference = 'Stop'
$base = if ($env:CRABSENSE_API) { $env:CRABSENSE_API.TrimEnd('/') } else { 'http://localhost:5080' }
$failures = New-Object System.Collections.Generic.List[string]

function Fail([string]$m) { $script:failures.Add($m); Write-Host "  FAIL  $m" -ForegroundColor Red }
function Pass([string]$m) { Write-Host "  ok    $m" -ForegroundColor Green }

function Login {
  $r = Invoke-RestMethod "$base/api/auth/login" -Method Post -ContentType 'application/json' `
        -Body (@{ username = 'owner'; password = 'Owner@123' } | ConvertTo-Json -Compress)
  return @{ Authorization = "Bearer $($r.data.accessToken)"; 'Content-Type' = 'application/json' }
}

function Post($path, $body, $h) {
  $json = if ($body -is [string]) { $body } else { $body | ConvertTo-Json -Depth 8 -Compress }
  try { return Invoke-RestMethod "$base$path" -Method Post -Headers $h -Body $json }
  catch {
    $resp = $_.Exception.Response; $detail = ''; $code = 'n/a'
    if ($resp) {
      $code = [int]$resp.StatusCode
      try { $detail = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch { }
    }
    throw "POST $path -> HTTP $code. Server: $detail || Body: $json"
  }
}

function Get1($path, $h) { return Invoke-RestMethod "$base$path" -Headers $h }

function BoxOverview($areaId, $boxId, $h) {
  $ov = (Get1 "/api/boxes/overview?farmingAreaId=$areaId&limit=500" $h).data.items
  return @($ov | Where-Object { $_.id -eq $boxId })[0]
}

# ─── psql: doc lai DB de kiem chung that su, khong tin moi API ---------------
$psqlExe = $env:CRABSENSE_PSQL
if (-not $psqlExe) {
  $psqlExe = @(Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe' -ErrorAction SilentlyContinue |
    Sort-Object { [int]($_.Directory.Parent.Name) } -Descending | ForEach-Object { $_.FullName })[0]
}
if (-not $psqlExe -or -not (Test-Path $psqlExe)) { throw "Khong tim thay psql.exe. Dat `$env:CRABSENSE_PSQL." }

$cfgPath = Join-Path $PSScriptRoot '..\..\src\CrabSenseBE.Api\appsettings.Local.json'
if (-not (Test-Path $cfgPath)) { throw "Khong thay $cfgPath" }
$cs = (([System.IO.File]::ReadAllText($cfgPath) -replace '^\uFEFF', '') | ConvertFrom-Json).ConnectionStrings.DefaultConnection
$kv = @{}
foreach ($p in $cs.Split(';')) { $i = $p.IndexOf('='); if ($i -gt 0) { $kv[$p.Substring(0, $i).Trim().ToLower()] = $p.Substring($i + 1).Trim() } }
$env:PGPASSWORD = $kv['password']; $env:PGSSLMODE = 'require'; $env:PGCONNECT_TIMEOUT = '30'
$pgUri = "postgresql://$([uri]::EscapeDataString($kv['username'])):$([uri]::EscapeDataString($kv['password']))@$($kv['host']):$($kv['port'])/$($kv['database'])?sslmode=require"

function SqlScalar([string]$text) {
  $prev = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $out = @($text | & $psqlExe $pgUri -v ON_ERROR_STOP=1 -t -A -f - 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "psql that bai (exit $LASTEXITCODE): $($out -join ' | ')" }
    return ($out -join "`n").Trim()
  }
  finally { $ErrorActionPreference = $prev }
}

Write-Host "API: $base"
Write-Host "psql: $psqlExe"
$h = Login

# ─── Don khu ao con lai tu lan chay loi truoc ───────────────────────────────
try {
  $all = Get1 '/api/farming-areas?limit=500' $h
  $items = if ($all.data.items) { @($all.data.items) } elseif ($all.data -is [array]) { @($all.data) } else { @() }
  foreach ($old in @($items | Where-Object { $_.name -like 'ZZ-Lot Test*' })) {
    try {
      Invoke-RestMethod "$base/api/farming-areas/$($old.id)`?cascade=true" -Method Delete -Headers $h | Out-Null
      Write-Host "Da don khu ao cu: $($old.name)"
    } catch { Write-Host "Chua don duoc $($old.name): $($_.Exception.Message)" -ForegroundColor Yellow }
  }
} catch { }
Write-Host ""

# ─── 1. Dung khu ao: 1 khu, 1 hang, 2 hop, 2 cua ────────────────────────────
$stamp = Get-Date -Format 'HHmmss'
$areaId = (Post '/api/farming-areas' @{ name = "ZZ-Lot Test $stamp"; description = 'verify-molting-flow' } $h).data.id
$rowId = (Post '/api/farming-rows' @{ farmingAreaId = $areaId; name = 'Day Z1'; capacity = 5 } $h).data.id
$boxA = (Post '/api/boxes' @{ farmingRowId = $rowId } $h).data.id
$boxB = (Post '/api/boxes' @{ farmingRowId = $rowId } $h).data.id

$lotId = (Post '/api/crab-lots' @{
    name = "ZZ Lot $stamp"; lotCode = "ZZ-LOT-$stamp"; quantity = 2
    importDate = (Get-Date).AddDays(-10).ToUniversalTime().ToString('o'); supplierName = 'verify'
  } $h).data.id
$crabA = (Post '/api/crabs' @{ crabLotId = $lotId; boxId = $boxA; tag = 'ZZ-A'; weightGram = 200; carapaceWidthMm = 55; carapaceLengthMm = 45 } $h).data.id
$crabB = (Post '/api/crabs' @{ crabLotId = $lotId; boxId = $boxB; tag = 'ZZ-B'; weightGram = 210; carapaceWidthMm = 57; carapaceLengthMm = 47 } $h).data.id

Write-Host "Khu ao: $areaId (1 hang, 2 hop, 2 cua)"
Write-Host ""

# ─── 2. SAP LOT => hop phai bao premolt (FE to TIM) ────────────────────────
Write-Host "=== 1. Danh dau sap lot => crabCondition=premolt (TIM) ==="
Post '/api/operations' @{
  type = 'feeding'; boxIds = @($boxA); crabIds = @($crabA)
  appetite = 'many'; foodType = 'Ca bien'; condition = 'premolt'
} $h | Out-Null

$ovA = BoxOverview $areaId $boxA $h
if ($ovA.crabCondition -eq 'premolt') { Pass "hop bao crabCondition=premolt" }
else { Fail "mong doi crabCondition=premolt, nhan duoc '$($ovA.crabCondition)'" }
if ($ovA.crabCount -eq 1) { Pass "hop van co 1 con cua" } else { Fail "crabCount mong doi 1, nhan $($ovA.crabCount)" }
if ($ovA.status -eq 'active') { Pass "boxStatus van la active (mau phai lay tu crabCondition)" }
else { Fail "boxStatus mong doi active, nhan '$($ovA.status)'" }
Write-Host ""

# ─── 3. HUONG 1 - XUAT BAN cua lot ─────────────────────────────────────────
Write-Host "=== 2. Huong 1: XUAT BAN cua lot (isSoftshell=true) ==="
Post '/api/harvest-vouchers' @{
  harvestDate = (Get-Date).ToUniversalTime().ToString('o')
  notes = "Xuat cua lot ZZ-A"
  farmingAreaId = $areaId
  performedByName = 'verify'
  lines = @(@{
      crabId = $crabA; weightGram = 200; grade = 'A'
      isSoftshell = $true; conditionLabel = 'Cua lot'; result = 'passed'
    })
} $h | Out-Null

$ovA2 = BoxOverview $areaId $boxA $h
if ($ovA2.crabCount -eq 0) { Pass "hop da tra ve TRONG (crabCount=0)" }
else { Fail "sau khi xuat ban, crabCount mong doi 0, nhan $($ovA2.crabCount)" }
if ($ovA2.isOccupied -eq $false) { Pass "isOccupied=false" } else { Fail "isOccupied mong doi false" }
if ([string]::IsNullOrEmpty($ovA2.crabCondition)) { Pass "hop khong con tinh trang cua (khong to mau nua)" }
else { Fail "sau khi xuat ban, crabCondition mong doi null, nhan '$($ovA2.crabCondition)'" }

$crabAStatus = (Get1 "/api/crabs/$crabA" $h).data.status
if ($crabAStatus -eq 'harvested') { Pass "cua A -> harvested" } else { Fail "cua A mong doi harvested, nhan '$crabAStatus'" }

# DB la nguon su that: khong duoc con allocation mo, BoxId phai null
$openAlloc = SqlScalar "SELECT count(*) FROM be.""CrabBoxAllocations"" WHERE ""CrabId""='$crabA' AND ""EndTime"" IS NULL;"
if ($openAlloc -eq '0') { Pass "DB: khong con allocation mo cho cua A" } else { Fail "DB: con $openAlloc allocation mo" }
$dbBoxId = SqlScalar "SELECT COALESCE(""BoxId""::text,'(null)') FROM be.""Crabs"" WHERE ""Id""='$crabA';"
if ($dbBoxId -eq '(null)') { Pass "DB: Crabs.BoxId da ve null" } else { Fail "DB: Crabs.BoxId van la $dbBoxId" }
Write-Host ""

# ─── 4. HUONG 2 - DE LAI NUOI TIEP ─────────────────────────────────────────
Write-Host "=== 3. Huong 2: DE LAI NUOI TIEP (van danh dau lot, khong xuat) ==="
Post '/api/operations' @{
  type = 'feeding'; boxIds = @($boxB); crabIds = @($crabB)
  appetite = 'little'; condition = 'premolt'
} $h | Out-Null

$ovB = BoxOverview $areaId $boxB $h
if ($ovB.crabCondition -eq 'premolt') { Pass "hop van bao premolt (TIM) sau khi chon de lai nuoi" }
else { Fail "mong doi premolt, nhan '$($ovB.crabCondition)'" }
if ($ovB.crabCount -eq 1 -and $ovB.isOccupied) { Pass "cua B van o trong hop" }
else { Fail "cua B phai con trong hop (count=$($ovB.crabCount), occupied=$($ovB.isOccupied))" }

$crabBStatus = (Get1 "/api/crabs/$crabB" $h).data.status
if ($crabBStatus -ne 'harvested' -and $crabBStatus -ne 'sold') { Pass "cua B khong bi xuat ban (status=$crabBStatus)" }
else { Fail "cua B bi doi trang thai ngoai y muon: '$crabBStatus'" }
$dbBoxB = SqlScalar "SELECT COALESCE(""BoxId""::text,'(null)') FROM be.""Crabs"" WHERE ""Id""='$crabB';"
if ($dbBoxB -eq $boxB) { Pass "DB: cua B van gan dung hop" } else { Fail "DB: cua B boxId mong doi $boxB, nhan $dbBoxB" }
Write-Host ""

# ─── 5. LOT THANH CONG THAT -> softshell (van TIM) ─────────────────────────
Write-Host "=== 4. Ghi nhan lot thanh cong => crabCondition=softshell (TIM) ==="
Post "/api/crabs/$crabB/moltings" @{
  crabId = $crabB; boxId = $boxB
  moltTime = (Get-Date).ToUniversalTime().ToString('o')
  result = 'success'; notes = 'verify-molting-flow'
} $h | Out-Null

$ovB2 = BoxOverview $areaId $boxB $h
if ($ovB2.crabCondition -eq 'softshell') { Pass "hop bao crabCondition=softshell (TIM)" }
else { Fail "mong doi crabCondition=softshell, nhan '$($ovB2.crabCondition)'" }
Write-Host ""

# ─── 6. NGUY CO = DO ───────────────────────────────────────────────────────
Write-Host "=== 5. Nguy co => problem / weak (DEU DO) ==="
Post '/api/operations' @{
  type = 'feeding'; boxIds = @($boxB); crabIds = @($crabB)
  appetite = 'none'; condition = 'attention'
} $h | Out-Null
$ovB3 = BoxOverview $areaId $boxB $h
if ($ovB3.crabCondition -eq 'problem') { Pass "condition=attention -> crabCondition=problem" }
else { Fail "mong doi problem, nhan '$($ovB3.crabCondition)'" }

Post '/api/operations' @{
  type = 'feeding'; boxIds = @($boxB); crabIds = @($crabB)
  appetite = 'none'; condition = 'weak'
} $h | Out-Null
$ovB4 = BoxOverview $areaId $boxB $h
if ($ovB4.crabCondition -eq 'weak') { Pass "condition=weak -> crabCondition=weak" }
else { Fail "mong doi weak, nhan '$($ovB4.crabCondition)'" }

# Ve lai binh thuong -> xanh
Post '/api/operations' @{
  type = 'feeding'; boxIds = @($boxB); crabIds = @($crabB)
  appetite = 'many'; condition = 'normal'
} $h | Out-Null
$ovB5 = BoxOverview $areaId $boxB $h
if ($ovB5.crabCondition -eq 'normal') { Pass "condition=normal -> crabCondition=normal (XANH)" }
else { Fail "mong doi normal, nhan '$($ovB5.crabCondition)'" }
Write-Host ""

# ─── 7. Don sach ───────────────────────────────────────────────────────────
Write-Host "=== 6. Don sach khu ao (cascade) ==="
Invoke-RestMethod "$base/api/farming-areas/$areaId`?cascade=true" -Method Delete -Headers $h | Out-Null
$left = SqlScalar "SELECT count(*) FROM be.""FarmingAreas"" WHERE ""Id""='$areaId';"
if ($left -eq '0') { Pass "da xoa khu ao (kiem tra ca DB)" } else { Fail "khu ao van con trong DB" }
Write-Host ""

if ($failures.Count -eq 0) {
  Write-Host "TAT CA DEU PASS" -ForegroundColor Green
  exit 0
}
Write-Host "$($failures.Count) MUC FAIL:" -ForegroundColor Red
$failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
