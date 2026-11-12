param(
    [string]$PostgresBin = "",
    [int]$Port = 55440
)

$ErrorActionPreference = "Stop"
$windows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
$exe = if ($windows) { ".exe" } else { "" }
if ([string]::IsNullOrWhiteSpace($PostgresBin)) {
    if ($windows) {
        $PostgresBin = Get-ChildItem "C:\Program Files\PostgreSQL" -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "bin" } |
            Where-Object { Test-Path (Join-Path $_ "initdb.exe") } |
            Select-Object -First 1
    } else {
        $pgConfig = Get-Command pg_config -ErrorAction SilentlyContinue
        if ($pgConfig) { $PostgresBin = (& $pgConfig.Source --bindir).Trim() }
    }
}
if ([string]::IsNullOrWhiteSpace($PostgresBin)) { throw "PostgreSQL binaries not found." }
$initdb = Join-Path $PostgresBin "initdb$exe"
$pgctl = Join-Path $PostgresBin "pg_ctl$exe"
$createdb = Join-Path $PostgresBin "createdb$exe"
$psql = Join-Path $PostgresBin "psql$exe"
foreach ($binary in @($initdb, $pgctl, $createdb, $psql)) {
    if (!(Test-Path $binary)) { throw "Missing binary: $binary" }
}
$data = Join-Path ([System.IO.Path]::GetTempPath()) ("crabsense-migration-" + [Guid]::NewGuid().ToString("N"))
$oldConnection = $env:ConnectionStrings__DefaultConnection
$oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
$oldDotnetEnvironment = $env:DOTNET_ENVIRONMENT
$oldJwt = $env:Jwt__Secret
$started = $false
function Assert-Exit([string]$Message) {
    if ($LASTEXITCODE -ne 0) { throw "$Message (exit $LASTEXITCODE)" }
}
function Invoke-Psql([string]$Database, [string]$Sql) {
    $output = $Sql | & $psql -X -q -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port -U postgres -d $Database -At 2>&1
    Assert-Exit "SQL assertion failed"
    return ($output -join "`n").Trim()
}
function Update-Database([string]$Database, [string]$Target = "") {
    $env:ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=$Port;Database=$Database;Username=postgres"
    $arguments = @("ef","database","update")
    if ($Target) { $arguments += $Target }
    $arguments += @("--project","src/CrabSenseBE.Infrastructure/CrabSenseBE.Infrastructure.csproj",
        "--startup-project","src/CrabSenseBE.Api/CrabSenseBE.Api.csproj",
        "--context","AppDbContext","--configuration","Release","--no-build")
    & dotnet @arguments
    Assert-Exit "EF database update failed for $Database"
}

try {
    & $initdb -D $data --auth=trust --username=postgres --encoding=UTF8 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "initdb failed (exit $LASTEXITCODE)" }
    $serverLog = Join-Path $data "server.log"
    $serverOptions = "-p $Port -F -h 127.0.0.1"
    # Ubuntu packages default to /var/run/postgresql, which the runner may not own.
    # All test clients use TCP; do not create a system Unix-domain socket.
    if (!$windows) { $serverOptions += " -c unix_socket_directories=" }
    & $pgctl -D $data -l $serverLog -o $serverOptions -w -t 60 start
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path $serverLog) { Get-Content $serverLog | Write-Host }
        throw "PostgreSQL test server failed to start; see server.log above."
    }
    $started = $true
    & $psql -X -q -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port -U postgres -d postgres -c "SELECT 1"
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL TCP readiness check failed." }
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    $env:DOTNET_ENVIRONMENT = "Production"
    $env:Jwt__Secret = "migration-test-only-local-secret-not-for-production-2026"

    & dotnet build src/CrabSenseBE.Api/CrabSenseBE.Api.csproj -c Release --no-restore
    Assert-Exit "Could not compile migration assembly"

    & $createdb -h 127.0.0.1 -p $Port -U postgres clean_migration
    Assert-Exit "Could not create clean database"
    Update-Database "clean_migration"

    # Build an actual previous EF schema, not a hand-written subset.
    & $createdb -h 127.0.0.1 -p $Port -U postgres legacy_migration
    Assert-Exit "Could not create legacy database"
    Update-Database "legacy_migration" "20260924164600_TrainingDataEvents"
    $fixture = @'
INSERT INTO be."SyncInboxItems"
("Id","IdempotencyKey","EntityType","EntityId","OperationType","PayloadJson","Status","ReceivedAt","ProcessedAt")
VALUES
('60000000-0000-0000-0000-000000000001','legacy-key','crab','legacy-crab','create_crab','{}','processed',now(),now());
'@
    $fixture | & $psql -X -q -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port -U postgres -d legacy_migration | Out-Null
    Assert-Exit "Could not insert legacy inbox evidence"

    # Validate the historic EdgeCommands duplicate-table fix against a populated inbox.
    Update-Database "legacy_migration" "20260926075139_EdgeCommands"
    Update-Database "legacy_migration" "20260924164600_TrainingDataEvents"
    $inboxBeforeUpgrade = Invoke-Psql "legacy_migration" 'SELECT "Status" FROM be."SyncInboxItems" WHERE "IdempotencyKey" = ''legacy-key'';'
    if ($inboxBeforeUpgrade -ne "processed") { throw "EdgeCommands rollback removed or changed the earlier sync inbox." }
    Update-Database "legacy_migration" "20260926075139_EdgeCommands"

    # Some deployed developer schemas retained the nullable FoodResponse field.
    Invoke-Psql "legacy_migration" 'ALTER TABLE be."TrainingLabels" ADD COLUMN IF NOT EXISTS "FoodResponse" text NULL;' | Out-Null
    Invoke-Psql "legacy_migration" 'INSERT INTO be."TrainingLabels" ("Id","LabelSource","LabeledAt","CreatedAt","FoodResponse") VALUES (''60000000-0000-0000-0000-000000000002'',''legacy'',now(),now(),''legacy-label'');' | Out-Null
    Update-Database "legacy_migration" "20261003105028_AddFrozenCrabItems"
    $legacyLabel = Invoke-Psql "legacy_migration" 'SELECT "FoodResponse" FROM be."TrainingLabels" WHERE "Id" = ''60000000-0000-0000-0000-000000000002'';'
    if ($legacyLabel -ne "legacy-label") { throw "Legacy training annotation was lost: $legacyLabel" }

    # Reproduce the old missing-table condition only in this disposable test DB.
    Invoke-Psql "legacy_migration" 'DROP TABLE be."FrozenCrabItems";' | Out-Null
    Update-Database "legacy_migration" "20261007084724_RepairMissingFrozenCrabItems"
    $repairedTable = Invoke-Psql "legacy_migration" 'SELECT to_regclass(''be."FrozenCrabItems"'') IS NOT NULL;'
    if ($repairedTable -ne "t") { throw "FrozenCrabItems repair did not restore the missing table." }
    $repairIndexes = Invoke-Psql "legacy_migration" 'SELECT count(*) FROM pg_indexes WHERE schemaname = ''be'' AND tablename = ''FrozenCrabItems'' AND indexname IN (''IX_FrozenCrabItems_BarcodeValue'', ''IX_FrozenCrabItems_CrabId'', ''IX_FrozenCrabItems_FrozenLotId'', ''IX_FrozenCrabItems_HarvestLineId'');'
    if ($repairIndexes -ne "4") { throw "FrozenCrabItems repair indexes incomplete: $repairIndexes" }
    Update-Database "legacy_migration" "20261003105028_AddFrozenCrabItems"
    if ((Invoke-Psql "legacy_migration" 'SELECT to_regclass(''be."FrozenCrabItems"'') IS NOT NULL;') -ne "t") { throw "Repair rollback removed the earlier inventory table." }

    Update-Database "legacy_migration" "20261112012203_AddEdgeCommandRequestSource"
    Update-Database "legacy_migration"
    $legacyRow = Invoke-Psql "legacy_migration" 'SELECT "IdempotencyKey" || ''|'' || coalesce("ActorUserId"::text,''NULL'') || ''|'' || "RequestHash" || ''|'' || "Status" FROM be."SyncInboxItems" WHERE "IdempotencyKey" = ''legacy-key'';'
    if ($legacyRow -ne "legacy-key|NULL||processed") { throw "Legacy inbox history changed: $legacyRow" }
    $finalLegacyLabel = Invoke-Psql "legacy_migration" 'SELECT "FoodResponse" FROM be."TrainingLabels" WHERE "Id" = ''60000000-0000-0000-0000-000000000002'';'
    if ($finalLegacyLabel -ne "legacy-label") { throw "Legacy training annotation changed after full upgrade: $finalLegacyLabel" }
    $cleanHistory = Invoke-Psql "clean_migration" 'SELECT "MigrationId" FROM be."__EFMigrationsHistory" ORDER BY "MigrationId";'
    $legacyHistory = Invoke-Psql "legacy_migration" 'SELECT "MigrationId" FROM be."__EFMigrationsHistory" ORDER BY "MigrationId";'
    if ($cleanHistory -ne $legacyHistory) { throw "Clean and upgraded migration history differ." }
    Write-Output "CLEAN_MIGRATION_PASSED=1"
    Write-Output "LEGACY_FULL_UPGRADE_PASSED=1"
}
finally {
    $env:ConnectionStrings__DefaultConnection = $oldConnection
    $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment
    $env:DOTNET_ENVIRONMENT = $oldDotnetEnvironment
    $env:Jwt__Secret = $oldJwt
    if ($started) { & $pgctl -D $data stop -m fast -w | Out-Null }
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
}
