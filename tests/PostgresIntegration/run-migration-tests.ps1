param(
    [string]$PostgresBin = "C:\Program Files\PostgreSQL\18\bin",
    [int]$Port = 55440
)

$ErrorActionPreference = "Stop"
$data = Join-Path $env:TEMP "crabsense-migration-pg"
$initdb = Join-Path $PostgresBin "initdb.exe"
$pgctl = Join-Path $PostgresBin "pg_ctl.exe"
$createdb = Join-Path $PostgresBin "createdb.exe"
$psql = Join-Path $PostgresBin "psql.exe"

if (Test-Path $data) {
    if (Test-Path (Join-Path $data "postmaster.pid")) {
        & $pgctl -D $data stop -m immediate | Out-Null
    }
    Remove-Item $data -Recurse -Force
}

& $initdb -D $data --auth=trust --username=postgres --encoding=UTF8 | Out-Null
& $pgctl -D $data -l (Join-Path $data "server.log") -o "-p $Port -F" -w start

function Invoke-Psql([string]$Database, [string]$Sql) {
    $output = $Sql | & $psql -X -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port `
        -U postgres -d $Database -At 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join [Environment]::NewLine)
    }
    return ($output -join "`n")
}

try {
    & dotnet build src/CrabSenseBE.Api/CrabSenseBE.Api.csproj -c Debug --no-restore | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not compile the current migration assembly."
    }

    & $createdb -h 127.0.0.1 -p $Port -U postgres clean_migration
    $env:ConnectionStrings__DefaultConnection = `
        "Host=127.0.0.1;Port=$Port;Database=clean_migration;Username=postgres"
    $cleanOutput = & dotnet ef database update `
        --project src/CrabSenseBE.Infrastructure/CrabSenseBE.Infrastructure.csproj `
        --startup-project src/CrabSenseBE.Api/CrabSenseBE.Api.csproj `
        --context AppDbContext --no-build 2>&1
    $cleanExit = $LASTEXITCODE

    & $createdb -h 127.0.0.1 -p $Port -U postgres legacy_migration
    $legacyFixture = @'
CREATE SCHEMA be;
CREATE TABLE be."SyncInboxItems" (
  "Id" uuid PRIMARY KEY,
  "IdempotencyKey" text NOT NULL,
  "EntityType" text NOT NULL,
  "EntityId" text NOT NULL,
  "OperationType" text NOT NULL,
  "BaseVersion" integer NULL,
  "ClientUpdatedAt" timestamptz NULL,
  "PayloadJson" jsonb NOT NULL,
  "Status" text NOT NULL,
  "ErrorMessage" text NULL,
  "ReceivedAt" timestamptz NOT NULL,
  "ProcessedAt" timestamptz NULL
);
CREATE UNIQUE INDEX "IX_SyncInboxItems_IdempotencyKey"
ON be."SyncInboxItems" ("IdempotencyKey");
INSERT INTO be."SyncInboxItems"
("Id","IdempotencyKey","EntityType","EntityId","OperationType","PayloadJson","Status","ReceivedAt","ProcessedAt")
VALUES
('60000000-0000-0000-0000-000000000001','legacy-key','crab','legacy-crab','create_crab','{}','processed',now(),now());
CREATE TABLE be."__EFMigrationsHistory" (
  "MigrationId" character varying(150) NOT NULL PRIMARY KEY,
  "ProductVersion" character varying(32) NOT NULL
);
INSERT INTO be."__EFMigrationsHistory" VALUES
('20261112012203_AddEdgeCommandRequestSource','8.0.22');
'@
    $legacyFixture | & $psql -X -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port `
        -U postgres -d legacy_migration | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the legacy migration fixture."
    }
    $legacyIndex = Invoke-Psql "legacy_migration" `
        "SELECT count(*) FROM pg_indexes WHERE schemaname='be' AND indexname='IX_SyncInboxItems_IdempotencyKey';"
    if ($legacyIndex -ne "1") {
        $allIndexes = Invoke-Psql "legacy_migration" `
            "SELECT schemaname || '.' || indexname FROM pg_indexes WHERE tablename='SyncInboxItems' ORDER BY indexname;"
        throw "Legacy idempotency index fixture was not created. count=$legacyIndex indexes=$allIndexes"
    }

    $upScript = & dotnet ef migrations script `
        20261112012203_AddEdgeCommandRequestSource `
        20261112025840_ScopeSyncIdempotencyByActor `
        --project src/CrabSenseBE.Infrastructure/CrabSenseBE.Infrastructure.csproj `
        --startup-project src/CrabSenseBE.Api/CrabSenseBE.Api.csproj `
        --context AppDbContext --no-build
    $upScript | & $psql -X -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port `
        -U postgres -d legacy_migration | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Latest migration failed on the legacy schema."
    }

    $legacyRow = Invoke-Psql "legacy_migration" `
        'SELECT "IdempotencyKey" || ''|'' || coalesce("ActorUserId"::text,''NULL'') || ''|'' || "RequestHash" || ''|'' || "Status" FROM be."SyncInboxItems";'
    if ($legacyRow -ne "legacy-key|NULL||processed") {
        throw "Legacy inbox history was changed: $legacyRow"
    }

    $downScript = & dotnet ef migrations script `
        20261112025840_ScopeSyncIdempotencyByActor `
        20261112012203_AddEdgeCommandRequestSource `
        --project src/CrabSenseBE.Infrastructure/CrabSenseBE.Infrastructure.csproj `
        --startup-project src/CrabSenseBE.Api/CrabSenseBE.Api.csproj `
        --context AppDbContext --no-build
    $downScript | & $psql -X -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port `
        -U postgres -d legacy_migration | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Latest migration rollback failed on the legacy schema."
    }
    if ((Invoke-Psql "legacy_migration" `
            'SELECT "IdempotencyKey" || ''|'' || "Status" FROM be."SyncInboxItems";') `
        -ne "legacy-key|processed") {
        throw "Rollback changed the legacy inbox row."
    }

    Write-Output "LEGACY_MIGRATION_AND_ROLLBACK_PASSED=1"
    if ($cleanExit -ne 0) {
        Write-Output "CLEAN_MIGRATION_FAILED=1"
        Write-Output ($cleanOutput -join [Environment]::NewLine)
        exit 1
    }
    Write-Output "CLEAN_MIGRATION_PASSED=1"
}
finally {
    Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
    & $pgctl -D $data stop -m fast -w
}
