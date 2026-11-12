param(
    [string]$PostgresBin = "",
    [int]$Port = 55440,
    [string]$MigrationBundle = ""
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
    if ($LASTEXITCODE -ne 0) {
        $exit = $LASTEXITCODE
        throw "SQL assertion failed for $Database (exit $exit): $($output -join [Environment]::NewLine)"
    }
    return ($output -join "`n").Trim()
}
function Update-Database([string]$Database, [string]$Target = "") {
    $env:ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=$Port;Database=$Database;Username=postgres"
    if ($MigrationBundle -and !$Target) {
        # Test the same self-contained artifact and environment-based connection
        # used on VPS. Historical target/rollback checks still use EF CLI.
        $bundle = (Resolve-Path $MigrationBundle).Path
        $oldAspRoot = $env:ASPNETCORE_CONTENTROOT
        $oldDotnetRoot = $env:DOTNET_CONTENTROOT
        Push-Location (Split-Path $bundle -Parent)
        try {
            $env:ASPNETCORE_CONTENTROOT = (Get-Location).Path
            $env:DOTNET_CONTENTROOT = (Get-Location).Path
            & $bundle
            Assert-Exit "EF bundle update failed for $Database"
        }
        finally {
            Pop-Location
            $env:ASPNETCORE_CONTENTROOT = $oldAspRoot
            $env:DOTNET_CONTENTROOT = $oldDotnetRoot
        }
        return
    }
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

    Update-Database "legacy_migration" "20261112081944_AddAlertIncidentIdentity"
    Invoke-Psql "legacy_migration" @'
INSERT INTO be."EdgeCommands" ("Id","DeviceCode","Command","Channel","Status","CreatedAt","ExpiresAt","AttemptCount","ConcurrencyVersion","RequestSource")
VALUES ('60000000-0000-0000-0000-000000000003','legacy-esp','on','1','pending',now(),now()+interval '5 minutes',0,0,'manual');
'@ | Out-Null
    Update-Database "legacy_migration"
    $legacyCommand = Invoke-Psql "legacy_migration" 'SELECT "Status" || ''|'' || ("CommandSequence" > 0)::text FROM be."EdgeCommands" WHERE "Id" = ''60000000-0000-0000-0000-000000000003'';'
    if ($legacyCommand -ne "superseded|true") { throw "Legacy pending command was not safely retired: $legacyCommand" }
    $legacyRow = Invoke-Psql "legacy_migration" 'SELECT "IdempotencyKey" || ''|'' || coalesce("ActorUserId"::text,''NULL'') || ''|'' || "RequestHash" || ''|'' || "Status" FROM be."SyncInboxItems" WHERE "IdempotencyKey" = ''legacy-key'';'
    if ($legacyRow -ne "legacy-key|NULL||processed") { throw "Legacy inbox history changed: $legacyRow" }
    $finalLegacyLabel = Invoke-Psql "legacy_migration" 'SELECT "FoodResponse" FROM be."TrainingLabels" WHERE "Id" = ''60000000-0000-0000-0000-000000000002'';'
    if ($finalLegacyLabel -ne "legacy-label") { throw "Legacy training annotation changed after full upgrade: $finalLegacyLabel" }
    $cleanHistory = Invoke-Psql "clean_migration" 'SELECT "MigrationId" FROM be."__EFMigrationsHistory" ORDER BY "MigrationId";'
    $legacyHistory = Invoke-Psql "legacy_migration" 'SELECT "MigrationId" FROM be."__EFMigrationsHistory" ORDER BY "MigrationId";'
    if ($cleanHistory -ne $legacyHistory) { throw "Clean and upgraded migration history differ." }
    # Test the actual migrated EF schema (not a handcrafted fixture schema).
    Invoke-Psql "clean_migration" @'
DO $$
DECLARE
    admin_id uuid := '70000000-0000-0000-0000-000000000001';
    manager_id uuid := '70000000-0000-0000-0000-000000000002';
    staff_id uuid := '70000000-0000-0000-0000-000000000003';
    area_a uuid := '70000000-0000-0000-0000-000000000010';
    area_b uuid := '70000000-0000-0000-0000-000000000020';
    cmd_a uuid := '70000000-0000-0000-0000-000000000101';
    cmd_b uuid := '70000000-0000-0000-0000-000000000102';
    cmd_other uuid := '70000000-0000-0000-0000-000000000103';
    cmd_alloff uuid := '70000000-0000-0000-0000-000000000104';
    seq_a bigint;
    seq_b bigint;
BEGIN
    INSERT INTO be."AppUsers" ("Id","Username","Email","FullName","PasswordHash","Role","IsActive","CreatedAt","TokenVersion","RefreshTokenVersion")
    VALUES (admin_id,'test-fa','fa@example.test','FA','test','FarmAdministrator',true,now(),0,0),
           (manager_id,'test-fm','fm@example.test','FM','test','FarmManager',true,now(),0,0),
           (staff_id,'test-fs','fs@example.test','FS','test','FarmStaff',true,now(),0,0);
    INSERT INTO be."FarmingAreas" ("Id","Code","Name","Address","IsActive","Status","CreatedAt")
    VALUES (area_a,'TEST-AREA-A','Area A','test',true,'Active',now()),
           (area_b,'TEST-AREA-B','Area B','test',true,'Active',now());
    INSERT INTO be."FarmManagerAssignments" ("Id","ManagerUserId","FarmingAreaId","AssignedByUserId","CreatedAt")
    VALUES ('70000000-0000-0000-0000-000000000030',manager_id,area_a,admin_id,now());

    BEGIN
        INSERT INTO be."FarmStaffAssignments" ("Id","StaffUserId","ManagerUserId","FarmingAreaId","AssignedByUserId","CreatedAt")
        VALUES ('70000000-0000-0000-0000-000000000040',staff_id,manager_id,area_b,admin_id,now());
        RAISE EXCEPTION 'Mismatched manager-area pair accepted';
    EXCEPTION WHEN foreign_key_violation THEN
        NULL;
    END;
    INSERT INTO be."FarmStaffAssignments" ("Id","StaffUserId","ManagerUserId","FarmingAreaId","AssignedByUserId","CreatedAt")
    VALUES ('70000000-0000-0000-0000-000000000040',staff_id,manager_id,area_a,admin_id,now());
    BEGIN
        DELETE FROM be."FarmManagerAssignments" WHERE "ManagerUserId"=manager_id AND "FarmingAreaId"=area_a;
        RAISE EXCEPTION 'Assignment with dependent staff was deleted';
    EXCEPTION WHEN foreign_key_violation OR restrict_violation THEN NULL;
    END;

    INSERT INTO be."NotificationRules" ("Id","TypeCode","DisplayName","IsEnabled","RecipientRoles","ScopeByArea","ChannelCodes","CreatedAt","FarmingAreaId")
    VALUES ('70000000-0000-0000-0000-000000000050','scope-test','Test',true,'FarmAdministrator',true,'in_app',now(),NULL),
           ('70000000-0000-0000-0000-000000000051','scope-test','Test',false,'FarmAdministrator',true,'in_app',now(),area_a),
           ('70000000-0000-0000-0000-000000000052','scope-test','Test',true,'FarmAdministrator',true,'in_app',now(),area_b);
    BEGIN
        INSERT INTO be."NotificationRules" ("Id","TypeCode","DisplayName","IsEnabled","RecipientRoles","ScopeByArea","ChannelCodes","CreatedAt","FarmingAreaId")
        VALUES ('70000000-0000-0000-0000-000000000053','scope-test','Test',true,'FarmAdministrator',true,'in_app',now(),area_a);
        RAISE EXCEPTION 'Duplicate area rule accepted';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;

    INSERT INTO be."EdgeCommands" ("Id","DeviceCode","Command","Channel","Status","CreatedAt","ExpiresAt","AttemptCount","ConcurrencyVersion","RequestSource")
    VALUES (cmd_a,'test-esp','on','1','delivered',now(),now()+interval '5 minutes',1,0,'manual')
    RETURNING "CommandSequence" INTO seq_a;
    INSERT INTO be."EdgeCommands" ("Id","DeviceCode","Command","Channel","Status","CreatedAt","ExpiresAt","AttemptCount","ConcurrencyVersion","RequestSource")
    VALUES (cmd_other,'test-esp','on','2','pending',now(),now()+interval '5 minutes',0,0,'manual');
    INSERT INTO be."EdgeCommands" ("Id","DeviceCode","Command","Channel","Status","CreatedAt","ExpiresAt","AttemptCount","ConcurrencyVersion","RequestSource")
    VALUES (cmd_b,'test-esp','off','1','pending',now(),now()+interval '5 minutes',0,0,'manual')
    RETURNING "CommandSequence" INTO seq_b;
    IF seq_a <= 0 OR seq_b <= seq_a THEN RAISE EXCEPTION 'Sequence is not monotonic'; END IF;
    IF NOT EXISTS (SELECT 1 FROM be."EdgeCommands" WHERE "Id"=cmd_a AND "Status"='superseded'
        AND "SupersededByCommandId"=cmd_b AND "ConcurrencyVersion"=1 AND "DeliveryAttemptId" IS NULL)
        THEN RAISE EXCEPTION 'Older same-channel command not invalidated'; END IF;
    IF NOT EXISTS (SELECT 1 FROM be."EdgeCommands" WHERE "Id"=cmd_other AND "Status"='pending')
        THEN RAISE EXCEPTION 'Unrelated channel was incorrectly superseded'; END IF;
    INSERT INTO be."EdgeCommands" ("Id","DeviceCode","Command","Channel","Status","CreatedAt","ExpiresAt","AttemptCount","ConcurrencyVersion","RequestSource")
    VALUES (cmd_alloff,'test-esp','alloff',NULL,'pending',now(),now()+interval '5 minutes',0,0,'manual');
    IF EXISTS (SELECT 1 FROM be."EdgeCommands" WHERE "DeviceCode"='test-esp' AND "Id"<>cmd_alloff
        AND "Status" IN ('pending','delivered'))
        THEN RAISE EXCEPTION 'Alloff did not invalidate older commands'; END IF;
END $$;
'@ | Out-Null
    Invoke-Psql "clean_migration" @'
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_Customers_FarmingAreas_FarmingAreaId')
        THEN RAISE EXCEPTION 'Customer area ownership FK missing'; END IF;
    BEGIN
        INSERT INTO be."Customers" ("Id","Name","CustomerType","IsActive","CreatedAt","FarmingAreaId")
        VALUES ('70000000-0000-0000-0000-000000000080','Customer','retail',true,now(),'70000000-0000-0000-0000-000000000099');
        RAISE EXCEPTION 'Invalid customer area accepted';
    EXCEPTION WHEN foreign_key_violation THEN NULL;
    END;
END $$;
'@ | Out-Null
    Invoke-Psql "clean_migration" @'
DO $$
DECLARE
    area_a uuid := '70000000-0000-0000-0000-000000000010';
    area_b uuid := '70000000-0000-0000-0000-000000000020';
    sensor_a uuid := '70000000-0000-0000-0000-000000000201';
    sensor_b uuid := '70000000-0000-0000-0000-000000000202';
BEGIN
    INSERT INTO be."FarmSettings" ("Id","Key","Value","Category","CreatedAt","FarmingAreaId")
    VALUES (gen_random_uuid(),'scope-test','A','general',now(),area_a),
           (gen_random_uuid(),'scope-test','B','general',now(),area_b);
    BEGIN
        INSERT INTO be."FarmSettings" ("Id","Key","Value","Category","CreatedAt","FarmingAreaId")
        VALUES (gen_random_uuid(),'scope-test','duplicate','general',now(),area_a);
        RAISE EXCEPTION 'Duplicate farm area setting accepted';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;
    INSERT INTO be."Sensors" ("Id","SensorCode","SensorType","IsActive","CreatedAt")
    VALUES (sensor_a,'schema-sensor-a','PH',true,now()),
           (sensor_b,'schema-sensor-b','PH',true,now());
    INSERT INTO be."WaterMeasurements" ("Id","SensorId","Value","MeasuredAt","Source","CreatedAt","EdgeEventKey")
    VALUES (gen_random_uuid(),sensor_a,7.2,now(),'edge:test',now(),repeat('A',64)),
           (gen_random_uuid(),sensor_b,7.3,now(),'edge:test',now(),repeat('A',64));
    BEGIN
        INSERT INTO be."WaterMeasurements" ("Id","SensorId","Value","MeasuredAt","Source","CreatedAt","EdgeEventKey")
        VALUES (gen_random_uuid(),sensor_a,7.4,now(),'edge:test',now(),repeat('A',64));
        RAISE EXCEPTION 'Concurrent replay index missing';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;
    INSERT INTO be."UserPushTokens" ("Id","UserId","Token","Platform","IsActive","LastSeenAt","CreatedAt")
    VALUES (gen_random_uuid(),'70000000-0000-0000-0000-000000000001','same-device-token','android',true,now(),now());
    BEGIN
        INSERT INTO be."UserPushTokens" ("Id","UserId","Token","Platform","IsActive","LastSeenAt","CreatedAt")
        VALUES (gen_random_uuid(),'70000000-0000-0000-0000-000000000002','same-device-token','android',true,now(),now());
        RAISE EXCEPTION 'Active push token shared across accounts';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='FK_FrozenCrabItems_OutboundTransaction')
        THEN RAISE EXCEPTION 'Frozen outbound FK missing'; END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='be'
        AND table_name='BackupScheduleSettings' AND column_name='LastAttemptSlotUtc')
        THEN RAISE EXCEPTION 'Backup schedule execution history missing'; END IF;
END $$;
'@ | Out-Null
    Write-Output "NON_SALES_SCHEMA_TESTS_PASSED=1"
    Write-Output "CUSTOMER_AREA_SCHEMA_TESTS_PASSED=1"
    Write-Output "AREA_AND_EDGE_SCHEMA_TESTS_PASSED=1"
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
