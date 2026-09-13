# Verify ghi nhan cho an theo tung con cua
#   POST /api/operations { crabIds, appetite, foodType, condition }
#   GET  /api/operations/crab/{crabId}
#
# Kiem tra:
#   1. Phieu luu dung appetite / foodType / condition / crabIds
#   2. Danh dau tinh trang -> cap nhat luon Crabs.Condition
#   3. Moi lan doi tinh trang -> 1 dong CrabStatusHistories (Source=manual)
#   4. Danh dau lai CUNG tinh trang -> khong sinh them lich su (khong rac)
#   5. Nhan tieng Viet ('khong an', 'can chu y') duoc chuan hoa
#   6. GET /operations/crab/{id} tra dung lich su an
#   7. Ho so cua co su kien timeline kind=feeding
#   8. Xoa cascade khu -> CrabIdsJson duoc don sach
#
# Yeu cau: BE dang chay; co psql >= 17.
# Usage:
#   powershell -File .\verify-feeding-history.ps1
#   $env:CRABSENSE_API='http://localhost:5080'; powershell -File .\verify-feeding-history.ps1

$ErrorActionPreference = 'Stop'
$base = if ($env:CRABSENSE_API) { $env:CRABSENSE_API.TrimEnd('/') } else { 'http://localhost:5080' }
$failures = New-Object System.Collections.Generic.List[string]

function Fail([string]$m) { $script:failures.Add($m); Write-Host "  FAIL  $m" -ForegroundColor Red }
function Pass([string]$m) { Write-Host "  ok    $m" -ForegroundColor Green }

# ─── API helpers ────────────────────────────────────────────────────────────

function Login {
  $r = Invoke-RestMethod "$base/api/auth/login" -Method Post -ContentType 'application/json' `
        -Body (@{ username = 'owner'; password = 'Owner@123' } | ConvertTo-Json -Compress)
  return @{ Authorization = "Bearer $($r.data.accessToken)"; 'Content-Type' = 'application/json' }
}

function Post($path, $body, $h) {
  $json = if ($body -is [string]) { $body } else { $body | ConvertTo-Json -Depth 8 -Compress }
  try {
    return Invoke-RestMethod "$base$path" -Method Post -Headers $h -Body $json
  }
  catch {
    $resp = $_.Exception.Response
    $detail = ''
    $code = 'n/a'
    if ($resp) {
      $code = [int]$resp.StatusCode
      try { $detail = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch { }
    }
    throw "POST $path -> HTTP $code. Server: $detail || Body gui: $json"
  }
}

function Get1($path, $h) { return Invoke-RestMethod "$base$path" -Headers $h }

function StatusOf($path, $h) {
  try { Invoke-RestMethod "$base$path" -Headers $h | Out-Null; return 200 }
  catch { return [int]$_.Exception.Response.StatusCode }
}

# ─── psql helper ────────────────────────────────────────────────────────────

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

# psql tra ve stderr -> voi EAP=Stop se thanh loi terminating va giet script.
# Ha EAP xuong Continue quanh loi goi native, tu kiem exit code.
function Sql([string]$text) {
  $prev = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $out = @($text | & $psqlExe $pgUri -v ON_ERROR_STOP=1 -t -A -F '|' -f - 2>&1)
    if ($LASTEXITCODE -ne 0) {
      if ($env:CRABSENSE_DEBUG) { Set-Content "$env:TEMP\feeding-check.sql" $text -Encoding UTF8 }
      throw "psql that bai (exit $LASTEXITCODE): $($out -join ' | ')"
    }
    return $out
  }
  finally { $ErrorActionPreference = $prev }
}
function SqlScalar([string]$text) { return (@(Sql $text) -join "`n").Trim() }

Write-Host "API: $base"
Write-Host "psql: $psqlExe"
$h = Login

# Don khu ao con lai tu lan chay loi truoc
try {
  $all = Get1 '/api/farming-areas' $h
  $items = if ($all.data.items) { @($all.data.items) } elseif ($all.data -is [array]) { @($all.data) } else { @() }
  foreach ($old in @($items | Where-Object { $_.name -like 'ZZ-An Test*' })) {
    try {
      Invoke-RestMethod "$base/api/farming-areas/$($old.id)`?cascade=true" -Method Delete -Headers $h | Out-Null
      Write-Host "Da don khu ao cu: $($old.name)"
    }
    catch { Write-Host "Chua don duoc $($old.name): $($_.Exception.Message)" -ForegroundColor Yellow }
  }
} catch { }
Write-Host ""

# ─── 1. Dung khu ao ─────────────────────────────────────────────────────────

$stamp = Get-Date -Format 'HHmmss'
$area = Post '/api/farming-areas' @{ name = "ZZ-An Test $stamp"; description = 'verify-feeding-history' } $h
$areaId = $area.data.id
$row = Post '/api/farming-rows' @{ farmingAreaId = $areaId; name = 'Day Z1'; capacity = 5 } $h
$box = Post '/api/boxes' @{ farmingRowId = $row.data.id } $h
$boxId = $box.data.id
$lot = Post '/api/crab-lots' @{
  name = "ZZ Lot $stamp"; lotCode = "ZZ-LOT-$stamp"; quantity = 1
  importDate = (Get-Date).AddDays(-10).ToUniversalTime().ToString('o'); supplierName = 'verify'
} $h
$crab = Post '/api/crabs' @{
  crabLotId = $lot.data.id; boxId = $boxId; tag = 'ZZ-AN'; weightGram = 200
  carapaceWidthMm = 55; carapaceLengthMm = 45
} $h
$crabId = $crab.data.id
Write-Host "Khu ao: $areaId  hop: $boxId  cua: $crabId"
Write-Host ""

function CrabConditionDb { return SqlScalar "SELECT ""Condition"" FROM be.""Crabs"" WHERE ""Id""='$crabId';" }
function ManualHistoryCount { return [int](SqlScalar "SELECT count(*)::text FROM be.""CrabStatusHistories"" WHERE ""CrabId""='$crabId' AND ""Source""='manual';") }

# ─── 2. Ghi phieu cho an ────────────────────────────────────────────────────

Write-Host "Ghi phieu cho an (an it + sap lot):"
$op1 = Post '/api/operations' @{
  type = 'feeding'; boxIds = @($boxId); crabIds = @($crabId)
  appetite = 'little'; condition = 'premolt'; foodType = 'Ca tuoi'
  quantity = 8; unit = 'g'; notes = "verify-feeding $stamp"
} $h

$d = $op1.data
if ($d.appetite -eq 'little') { Pass "appetite luu dung: $($d.appetite)" }
else { Fail "appetite sai: '$($d.appetite)' (mong doi little)" }
if ($d.condition -eq 'premolt') { Pass "condition luu dung: $($d.condition)" }
else { Fail "condition sai: '$($d.condition)' (mong doi premolt)" }
if ($d.foodType -eq 'Ca tuoi') { Pass "foodType luu dung: $($d.foodType)" }
else { Fail "foodType sai: '$($d.foodType)'" }
if (@($d.crabIds).Count -eq 1 -and @($d.crabIds)[0] -eq $crabId) { Pass "crabIds luu dung" }
else { Fail "crabIds sai: $($d.crabIds -join ',')" }

# ─── 3. Tich hop: cap nhat tinh trang cua ───────────────────────────────────

Write-Host ""
Write-Host "Tich hop tinh trang cua:"
$cond = CrabConditionDb
if ($cond -eq 'Premolt') { Pass "Crabs.Condition -> Premolt" }
else { Fail "Crabs.Condition = '$cond' (mong doi Premolt)" }
if ((ManualHistoryCount) -eq 1) { Pass "CrabStatusHistories: 1 dong manual" }
else { Fail "CrabStatusHistories manual = $(ManualHistoryCount) (mong doi 1)" }

# ─── 4. Danh dau lai cung tinh trang -> khong them lich su ──────────────────

Write-Host ""
Write-Host "Danh dau lai CUNG tinh trang (khong nen sinh lich su rac):"
$op2 = Post '/api/operations' @{
  type = 'feeding'; boxIds = @($boxId); crabIds = @($crabId)
  appetite = 'many'; condition = 'premolt'
} $h
if ((ManualHistoryCount) -eq 1) { Pass "van la 1 dong lich su (dung)" }
else { Fail "lich su tang len $(ManualHistoryCount) du tinh trang khong doi" }

# ─── 5. Nhan tieng Viet duoc chuan hoa ──────────────────────────────────────

Write-Host ""
Write-Host "Chuan hoa nhan tieng Viet ('khong an' + 'can chu y'):"
$op3 = Post '/api/operations' @{
  type = 'feeding'; boxIds = @($boxId); crabIds = @($crabId)
  appetite = 'khong an'; condition = 'can chu y'
} $h
if ($op3.data.appetite -eq 'none') { Pass "appetite 'khong an' -> none" }
else { Fail "appetite 'khong an' -> '$($op3.data.appetite)' (mong doi none)" }
if ($op3.data.condition -eq 'attention') { Pass "condition 'can chu y' -> attention" }
else { Fail "condition 'can chu y' -> '$($op3.data.condition)' (mong doi attention)" }
$cond = CrabConditionDb
if ($cond -eq 'Problem') { Pass "Crabs.Condition -> Problem (tu attention)" }
else { Fail "Crabs.Condition = '$cond' (mong doi Problem)" }
if ((ManualHistoryCount) -eq 2) { Pass "CrabStatusHistories: 2 dong manual" }
else { Fail "CrabStatusHistories manual = $(ManualHistoryCount) (mong doi 2)" }

# ─── 6. Doc lich su an theo cua ─────────────────────────────────────────────

Write-Host ""
Write-Host "Doc lich su an theo cua:"
$feed = Get1 "/api/operations/crab/$crabId" $h
$list = @($feed.data)
if ($list.Count -eq 3) { Pass "GET /operations/crab/{id} -> 3 phieu" }
else { Fail "tra ve $($list.Count) phieu (mong doi 3)" }
if ($list.Count -ge 1 -and $list[0].appetite -eq 'none') { Pass "sap xep moi nhat truoc" }
elseif ($list.Count -ge 1) { Fail "phieu dau tien appetite='$($list[0].appetite)' (mong doi none)" }

# Phieu cua con cua khac khong duoc lot vao
$other = [guid]::NewGuid().ToString()
$feedOther = Get1 "/api/operations/crab/$other" $h
if (@($feedOther.data).Count -eq 0) { Pass "cua khac -> 0 phieu (khong lot du lieu)" }
else { Fail "cua khac tra ve $(@($feedOther.data).Count) phieu" }

# ─── 7. Timeline ho so cua co su kien cho an ─────────────────────────────────

Write-Host ""
Write-Host "Ho so cua:"
$profile = Get1 "/api/crabs/$crabId/profile" $h
$feedingEvents = @($profile.data.timeline | Where-Object { $_.kind -eq 'feeding' })
if ($feedingEvents.Count -ge 3) { Pass "timeline co $($feedingEvents.Count) su kien feeding" }
else { Fail "timeline chi co $($feedingEvents.Count) su kien feeding (mong doi >= 3)" }
if (@($feedingEvents | Where-Object { $_.title -like '*khong an*' -or $_.title -like '*không ăn*' }).Count -ge 1) {
  Pass "tieu de su kien co muc an tieng Viet"
}
else { Fail "khong thay tieu de 'khong an' trong timeline: $($feedingEvents.title -join ' / ')" }

# ─── 8. Xoa cascade -> don tham chieu mem CrabIdsJson ───────────────────────

Write-Host ""
Write-Host "Xoa cascade khu ao:"
$opIds = @($op1.data.id, $op2.data.id, $op3.data.id)
try {
  $res = Invoke-RestMethod "$base/api/farming-areas/$areaId`?cascade=true" -Method Delete -Headers $h
  Pass "cascade=true: $($res.message)"
}
catch {
  $body = ''
  try { $body = (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } catch { }
  Fail "cascade=true that bai: $($_.Exception.Message) $body"
}

if ((StatusOf "/api/crabs/$crabId" $h) -eq 404) { Pass "GET cua -> 404" }
else { Fail "cua van doc duoc qua API" }

$left = @(Sql "SELECT ""Id""::text || '|' || ""CrabIdsJson"" || '|' || ""BoxIdsJson"" FROM be.""FarmOperations"" WHERE ""Id"" IN ($(($opIds | ForEach-Object { "'$_'" }) -join ',')) ORDER BY 1;")
$bad = @($left | Where-Object { $_ -match '\|' } | Where-Object { ($_ -split '\|')[1] -ne '[]' -or ($_ -split '\|')[2] -ne '[]' })
if ($bad.Count -eq 0) { Pass "CrabIdsJson + BoxIdsJson cua $($left.Count) phieu deu duoc don sach" }
else { foreach ($b in $bad) { Fail "tham chieu chua don: $b" } }

$orphan = [int](SqlScalar "SELECT count(*)::text FROM be.""CrabStatusHistories"" WHERE ""CrabId""='$crabId';")
if ($orphan -eq 0) { Pass "lich su tinh trang cua cua ao da bi xoa theo" }
else { Fail "con $orphan dong CrabStatusHistories mo coi" }

# ─── Ket qua ────────────────────────────────────────────────────────────────

Write-Host ""
if ($failures.Count -eq 0) {
  Write-Host "PASS  ghi nhan cho an dung." -ForegroundColor Green
  $exit = 0
} else {
  Write-Host "FAIL  $($failures.Count) loi:" -ForegroundColor Red
  foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
  $exit = 1
}

if ($failures.Count -gt 0) {
  Write-Host ""
  Write-Host "Con lai khu ao $areaId - don tay bang SQL neu can." -ForegroundColor Yellow
}

Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
exit $exit
