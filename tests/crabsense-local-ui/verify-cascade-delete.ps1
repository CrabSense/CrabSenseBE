# Verify DELETE /api/farming-areas/{id}?cascade=true
#
# Dung 1 khu ao -> hang -> hop -> cua, gan them 1 FarmOperation tro toi cac hop do
# (tham chieu mem, khong FK), roi xoa cascade va kiem tra:
#   1. cascade=false van bi chan 409 khi khu con hang  (hanh vi cu khong doi)
#   2. cascade=true xoa sach khu/hang/hop/cua
#   3. khong con ban ghi mo coi o cac bang con
#   4. BoxIdsJson cua FarmOperation duoc don, van giu Id khong lien quan
#
# Yeu cau: BE dang chay; co psql >= 17.
# Usage:
#   powershell -File .\verify-cascade-delete.ps1
#   $env:CRABSENSE_API='http://localhost:5080'; powershell -File .\verify-cascade-delete.ps1

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

# psql tra ve stderr (vd: 1 bang khong ton tai) -> voi EAP=Stop se thanh loi terminating
# va giet ca script. Ha EAP xuong Continue quanh loi goi native, tu kiem exit code.
function Sql([string]$text) {
  $prev = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $out = @($text | & $psqlExe $pgUri -v ON_ERROR_STOP=1 -t -A -F '|' -f - 2>&1)
    if ($LASTEXITCODE -ne 0) {
      if ($env:CRABSENSE_DEBUG) { Set-Content "$env:TEMP\cascade-check.sql" $text -Encoding UTF8 }
      throw "psql that bai (exit $LASTEXITCODE): $($out -join ' | ')"
    }
    return $out
  }
  finally { $ErrorActionPreference = $prev }
}
function SqlScalar([string]$text) { return (@(Sql $text) -join "`n").Trim() }

# Danh sach cot tham chieu (bang, cot) can soi mo coi.
# Gom ca FK NO ACTION lan cot tham chieu mem khong FK.
$refsByCrab = @(
  @('AiDetections', 'CrabId'), @('HarvestLines', 'CrabId'), @('Inspections', 'CrabId'),
  @('MediaAssets', 'CrabId'), @('SalesOrderLines', 'CrabId'), @('MoltingRecords', 'CrabId'),
  @('CrabBoxAllocations', 'CrabId'), @('CrabMortalityRecords', 'CrabId'),
  @('CrabStatusHistories', 'CrabId'), @('CrabWeightHistories', 'CrabId'),
  @('CrabHarvestHistories', 'CrabId'), @('CrabAiAnalyses', 'CrabId'), @('QrCodes', 'CrabId')
)
$refsByBox = @(
  @('HarvestLines', 'BoxId'), @('MediaAssets', 'BoxId'), @('MoltingRecords', 'BoxId'),
  @('BoxStatusHistories', 'BoxId'), @('CrabBoxAllocations', 'BoxId'), @('AiDetections', 'BoxId'),
  @('CrabAiAnalyses', 'BoxId'), @('Inspections', 'BoxId'), @('SaleTransactions', 'BoxId'),
  @('QrCodes', 'BoxId')
)

function Quoted([string[]]$ids) { return ($ids | ForEach-Object { "'$_'" }) -join ',' }

Write-Host "API: $base"
Write-Host "psql: $psqlExe"
$h = Login

# Don sach khu ao con lai tu lan chay loi truoc (dung chinh endpoint dang test)
$existing = @()
try {
  $all = Get1 '/api/farming-areas' $h
  $items = if ($all.data.items) { @($all.data.items) } elseif ($all.data -is [array]) { @($all.data) } else { @() }
  $existing = @($items | Where-Object { $_.name -like 'ZZ-Xoa Test*' })
} catch { }
foreach ($old in $existing) {
  try {
    Invoke-RestMethod "$base/api/farming-areas/$($old.id)`?cascade=true" -Method Delete -Headers $h | Out-Null
    Write-Host "Da don khu ao cu: $($old.name)"
  }
  catch { Write-Host "Chua don duoc khu ao cu $($old.name): $($_.Exception.Message)" -ForegroundColor Yellow }
}
Write-Host ""

# ─── 1. Dung khu ao ─────────────────────────────────────────────────────────

$stamp = Get-Date -Format 'HHmmss'
$area = Post '/api/farming-areas' @{ name = "ZZ-Xoa Test $stamp"; description = 'verify-cascade-delete' } $h
$areaId = $area.data.id

$rowA = Post '/api/farming-rows' @{ farmingAreaId = $areaId; name = 'Day Z1'; capacity = 5 } $h
$rowB = Post '/api/farming-rows' @{ farmingAreaId = $areaId; name = 'Day Z2'; capacity = 5 } $h

$boxA = Post '/api/boxes' @{ farmingRowId = $rowA.data.id } $h
$boxB = Post '/api/boxes' @{ farmingRowId = $rowB.data.id } $h
$boxIds = @($boxA.data.id, $boxB.data.id)

$lot = Post '/api/crab-lots' @{
  name = "ZZ Lot $stamp"; lotCode = "ZZ-LOT-$stamp"; quantity = 2
  importDate = (Get-Date).AddDays(-10).ToUniversalTime().ToString('o'); supplierName = 'verify'
} $h
$crabA = Post '/api/crabs' @{ crabLotId = $lot.data.id; boxId = $boxIds[0]; tag = 'ZZ-A'; weightGram = 200; carapaceWidthMm = 55; carapaceLengthMm = 45 } $h
$crabB = Post '/api/crabs' @{ crabLotId = $lot.data.id; boxId = $boxIds[1]; tag = 'ZZ-B'; weightGram = 210; carapaceWidthMm = 57; carapaceLengthMm = 47 } $h
$crabIds = @($crabA.data.id, $crabB.data.id)

Write-Host "Khu ao: $areaId  (2 hang, 2 hop, 2 cua)"

# QR duoc tao tu dong khi tao hop (chi de bao — khong phai dieu kien pass/fail)
$qrCount = SqlScalar "SELECT count(*)::text FROM be.""QrCodes"" WHERE ""BoxId"" IN ($(Quoted $boxIds));"
Pass "QR tao kem hop: $qrCount"

# Tham chieu mem: FarmOperation tro toi 2 hop + 1 Id khong lien quan (phai duoc giu)
$keepId = [guid]::NewGuid().ToString()
$op = Post '/api/operations' @{
  type = 'note'; boxIds = @($boxIds[0], $boxIds[1], $keepId); notes = "verify-cascade-delete $stamp"
} $h
$opId = $op.data.id
$before = SqlScalar "SELECT ""BoxIdsJson"" FROM be.""FarmOperations"" WHERE ""Id""='$opId';"
Pass "FarmOperation $opId BoxIdsJson truoc: $before"

# ─── 2. cascade=false phai bi chan 409 ──────────────────────────────────────

$code = StatusOf "/api/farming-areas/$areaId" $h
if ($code -ne 200) { Fail "Khu ao khong doc duoc (HTTP $code)" }

try {
  Invoke-RestMethod "$base/api/farming-areas/$areaId" -Method Delete -Headers $h | Out-Null
  Fail "cascade=false KHONG bi chan du khu con hang (dang le 409)"
}
catch {
  $c = [int]$_.Exception.Response.StatusCode
  if ($c -eq 409) { Pass "cascade=false bi chan 409 dung nhu cu" }
  else { Fail "cascade=false tra HTTP $c, mong doi 409" }
}

# ─── 3. Xoa cascade ─────────────────────────────────────────────────────────

try {
  $res = Invoke-RestMethod "$base/api/farming-areas/$areaId`?cascade=true" -Method Delete -Headers $h
  Pass "cascade=true: $($res.message)"
}
catch {
  $body = ''
  try { $body = (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } catch { }
  Fail "cascade=true that bai: $($_.Exception.Message) $body"
}

# ─── 4. Kiem tra ────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "Kiem tra ket qua:"

if ((StatusOf "/api/farming-areas/$areaId" $h) -eq 404) { Pass "GET khu -> 404" }
else { Fail "Khu van doc duoc qua API" }

foreach ($pair in @(@('hop', $boxIds[0]), @('cua', $crabIds[0]))) {
  $path = if ($pair[0] -eq 'hop') { "/api/boxes/$($pair[1])" } else { "/api/crabs/$($pair[1])" }
  if ((StatusOf $path $h) -eq 404) { Pass "GET $($pair[0]) -> 404" }
  else { Fail "$($pair[0]) van doc duoc qua API" }
}

# 4a. Khong con ban ghi mo coi
$parts = New-Object System.Collections.Generic.List[string]
function AddCheck($table, $column, [string[]]$ids) {
  if ($ids.Count -eq 0) { return }
  $script:parts.Add("SELECT '$table.$column' AS k, count(*) AS n FROM be.""$table"" WHERE ""$column"" IN ($(Quoted $ids))")
}
foreach ($r in $refsByCrab) { AddCheck $r[0] $r[1] $crabIds }
foreach ($r in $refsByBox)  { AddCheck $r[0] $r[1] $boxIds }
# TraceabilityLinks tro toi QR cua hop -> soi qua join, khoi phai liet ke qrIds
if ($boxIds.Count -gt 0) {
  $parts.Add("SELECT 'TraceabilityLinks (QR cua hop)' AS k, count(*) AS n FROM be.""TraceabilityLinks"" t JOIN be.""QrCodes"" q ON q.""Id"" = t.""QrCodeId"" WHERE q.""BoxId"" IN ($(Quoted $boxIds))")
}
AddCheck 'Crabs' 'Id' $crabIds
AddCheck 'Boxes' 'Id' $boxIds
AddCheck 'FarmingRows' 'Id' @($rowA.data.id, $rowB.data.id)

$orphanSql = ($parts -join "`nUNION ALL`n") + "`nORDER BY 1;"
if ($env:CRABSENSE_DEBUG) { Set-Content "$env:TEMP\cascade-orphan-check.sql" $orphanSql -Encoding UTF8 }
$orphans = @(Sql $orphanSql | Where-Object { $_ -match '\|' } | Where-Object { [int]($_ -split '\|')[1] -ne 0 })
if ($orphans.Count -eq 0) { Pass "Khong co ban ghi mo coi ($($parts.Count) bang da soi)" }
else { foreach ($o in $orphans) { Fail "Mo coi con lai: $o" } }

# 4b. JSON tham chieu mem duoc don, Id khong lien quan con nguyen
$after = SqlScalar "SELECT ""BoxIdsJson"" FROM be.""FarmOperations"" WHERE ""Id""='$opId';"
$parsed = @()
try { $parsed = @($after | ConvertFrom-Json) } catch { }
if ($parsed.Count -eq 1 -and $parsed[0] -eq $keepId) { Pass "BoxIdsJson sau khi xoa: $after" }
else { Fail "BoxIdsJson sau khi xoa sai: '$after' (mong doi chi con $keepId)" }

# ─── Ket qua ────────────────────────────────────────────────────────────────

Write-Host ""
if ($failures.Count -eq 0) {
  Write-Host "PASS  cascade delete dung." -ForegroundColor Green
  $exit = 0
} else {
  Write-Host "FAIL  $($failures.Count) loi:" -ForegroundColor Red
  foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
  $exit = 1
}

# Don dep neu cascade that bai giua duong
if ($failures.Count -gt 0) {
  Write-Host ""
  Write-Host "Con lai du lieu khu ao $areaId — don tay bang SQL neu can." -ForegroundColor Yellow
}

Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
exit $exit
