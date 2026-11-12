param(
    [string]$PostgresBin = "C:\Program Files\PostgreSQL\18\bin",
    [int]$Port = 55439
)

$ErrorActionPreference = "Stop"
$data = Join-Path $env:TEMP "crabsense-hotfix-pg"
$initdb = Join-Path $PostgresBin "initdb.exe"
$pgctl = Join-Path $PostgresBin "pg_ctl.exe"
$psql = Join-Path $PostgresBin "psql.exe"
if (!(Test-Path $initdb) -or !(Test-Path $pgctl) -or !(Test-Path $psql)) {
    throw "PostgreSQL binaries were not found in $PostgresBin"
}

if (Test-Path $data) {
    if (Test-Path (Join-Path $data "postmaster.pid")) {
        & $pgctl -D $data stop -m immediate | Out-Null
    }
    Remove-Item $data -Recurse -Force
}
& $initdb -D $data --auth=trust --username=postgres --encoding=UTF8 |
    Out-Null
& $pgctl -D $data -l (Join-Path $data "server.log") `
    -o "-p $Port -F" -w start

function Invoke-Psql([string]$Sql, [switch]$AllowFailure) {
    $previousPreference = $ErrorActionPreference
    if ($AllowFailure) {
        $ErrorActionPreference = "Continue"
    }
    $result = & $psql -X -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port `
        -U postgres -d postgres -Atc $Sql 2>&1
    $exit = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference
    if (!$AllowFailure -and $exit -ne 0) {
        throw ($result -join [Environment]::NewLine)
    }
    [pscustomobject]@{ ExitCode = $exit; Output = ($result -join "`n") }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ("$Expected" -ne "$Actual") {
        throw "$Message Expected=$Expected Actual=$Actual"
    }
}

try {
    Invoke-Psql @'
CREATE SCHEMA be;
CREATE TABLE be."CrabLots" (
  "Id" uuid PRIMARY KEY,
  "Quantity" integer NOT NULL,
  "DeadOnArrival" integer NOT NULL DEFAULT 0,
  "ClosedAt" timestamptz NULL,
  "Status" text NOT NULL DEFAULT 'Allocating'
);
CREATE TABLE be."Boxes" ("Id" uuid PRIMARY KEY);
CREATE TABLE be."Crabs" (
  "Id" uuid PRIMARY KEY,
  "CrabLotId" uuid NOT NULL REFERENCES be."CrabLots"("Id"),
  "BoxId" uuid NULL REFERENCES be."Boxes"("Id"),
  "Status" integer NOT NULL
);
CREATE TABLE be."SalesOrderLines" (
  "Id" uuid PRIMARY KEY,
  "CrabId" uuid NULL,
  "ReleasedAt" timestamptz NULL
);
CREATE UNIQUE INDEX "IX_SalesOrderLines_CrabId"
ON be."SalesOrderLines" ("CrabId")
WHERE "CrabId" IS NOT NULL AND "ReleasedAt" IS NULL;

CREATE OR REPLACE FUNCTION be.enforce_crab_lot_registration()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE lot_record be."CrabLots"%ROWTYPE; registered_count integer;
BEGIN
  SELECT * INTO lot_record FROM be."CrabLots"
  WHERE "Id" = NEW."CrabLotId" FOR UPDATE;
  PERFORM pg_sleep(0.2);
  IF lot_record."ClosedAt" IS NOT NULL THEN
    RAISE EXCEPTION 'Crab lot is closed';
  END IF;
  SELECT COUNT(*) INTO registered_count FROM be."Crabs"
  WHERE "CrabLotId" = NEW."CrabLotId";
  IF registered_count + 1 >
     lot_record."Quantity" - lot_record."DeadOnArrival" THEN
    RAISE EXCEPTION 'Crab lot capacity exceeded';
  END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER "TR_Crabs_EnforceLotRegistration"
BEFORE INSERT ON be."Crabs" FOR EACH ROW
EXECUTE FUNCTION be.enforce_crab_lot_registration();

CREATE OR REPLACE FUNCTION be.enforce_one_live_crab_per_box()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF NEW."BoxId" IS NULL OR NEW."Status" NOT IN (0, 1, 3) THEN
    RETURN NEW;
  END IF;
  PERFORM 1 FROM be."Boxes" WHERE "Id" = NEW."BoxId" FOR UPDATE;
  PERFORM pg_sleep(0.2);
  IF EXISTS (
    SELECT 1 FROM be."Crabs"
    WHERE "BoxId" = NEW."BoxId" AND "Status" IN (0, 1, 3)
  ) THEN
    RAISE EXCEPTION 'Box already contains a live crab';
  END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER "TR_Crabs_OneLivePerBox"
BEFORE INSERT ON be."Crabs" FOR EACH ROW
EXECUTE FUNCTION be.enforce_one_live_crab_per_box();

CREATE TABLE be."SyncInboxItems" (
  "Id" uuid PRIMARY KEY,
  "ActorUserId" uuid NULL,
  "IdempotencyKey" text NOT NULL,
  "RequestHash" varchar(64) NOT NULL,
  "Status" text NOT NULL
);
CREATE UNIQUE INDEX "IX_SyncInboxItems_ActorUserId_IdempotencyKey"
ON be."SyncInboxItems" ("ActorUserId","IdempotencyKey")
WHERE "ActorUserId" IS NOT NULL;
CREATE UNIQUE INDEX "IX_SyncInboxItems_IdempotencyKey_Legacy"
ON be."SyncInboxItems" ("IdempotencyKey")
WHERE "ActorUserId" IS NULL;
CREATE TABLE be."SyncBoxes" (
  "Id" uuid PRIMARY KEY,
  "Status" text NOT NULL
);
CREATE TABLE be."SyncCrabs" (
  "Id" uuid PRIMARY KEY,
  "Code" text NOT NULL UNIQUE
);
'@ | Out-Null

    $lot = "10000000-0000-0000-0000-000000000001"
    Invoke-Psql "INSERT INTO be.`"CrabLots`" (`"Id`",`"Quantity`") VALUES ('$lot',1)" |
        Out-Null
    $registrationSql = @(
        "INSERT INTO be.`"Crabs`" (`"Id`",`"CrabLotId`",`"Status`") VALUES ('10000000-0000-0000-0000-000000000011','$lot',0)",
        "INSERT INTO be.`"Crabs`" (`"Id`",`"CrabLotId`",`"Status`") VALUES ('10000000-0000-0000-0000-000000000012','$lot',0)"
    )
    $registrationJobs = $registrationSql | ForEach-Object {
        Start-Job -ScriptBlock {
            param($Psql, $Port, $Sql)
            & $Psql -X -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port `
                -U postgres -d postgres -c $Sql *> $null
            $LASTEXITCODE
        } -ArgumentList $psql, $Port, $_
    }
    $registrationExit = $registrationJobs | Wait-Job | Receive-Job
    $registrationJobs | Remove-Job
    Assert-Equal 1 (Invoke-Psql "SELECT COUNT(*) FROM be.`"Crabs`" WHERE `"CrabLotId`"='$lot'").Output `
        "Concurrent registration exceeded lot capacity."
    Assert-Equal 1 (@($registrationExit | Where-Object { $_ -ne 0 }).Count) `
        "Exactly one concurrent registration must be rejected."

    $box = "20000000-0000-0000-0000-000000000001"
    $lotA = "20000000-0000-0000-0000-000000000002"
    $lotB = "20000000-0000-0000-0000-000000000003"
    Invoke-Psql @"
INSERT INTO be."Boxes" ("Id") VALUES ('$box');
INSERT INTO be."CrabLots" ("Id","Quantity") VALUES ('$lotA',2),('$lotB',2);
"@ | Out-Null
    $boxSql = @(
        "INSERT INTO be.`"Crabs`" (`"Id`",`"CrabLotId`",`"BoxId`",`"Status`") VALUES ('20000000-0000-0000-0000-000000000011','$lotA','$box',0)",
        "INSERT INTO be.`"Crabs`" (`"Id`",`"CrabLotId`",`"BoxId`",`"Status`") VALUES ('20000000-0000-0000-0000-000000000012','$lotB','$box',1)"
    )
    $boxJobs = $boxSql | ForEach-Object {
        Start-Job -ScriptBlock {
            param($Psql, $Port, $Sql)
            & $Psql -X -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port `
                -U postgres -d postgres -c $Sql *> $null
            $LASTEXITCODE
        } -ArgumentList $psql, $Port, $_
    }
    $boxExit = $boxJobs | Wait-Job | Receive-Job
    $boxJobs | Remove-Job
    Assert-Equal 1 (Invoke-Psql "SELECT COUNT(*) FROM be.`"Crabs`" WHERE `"BoxId`"='$box'").Output `
        "Two live crabs were placed in one box."
    Assert-Equal 1 (@($boxExit | Where-Object { $_ -ne 0 }).Count) `
        "Exactly one concurrent box placement must be rejected."

    $saleCrab = "30000000-0000-0000-0000-000000000001"
    $saleSql = @(
        "INSERT INTO be.`"SalesOrderLines`" (`"Id`",`"CrabId`") VALUES ('30000000-0000-0000-0000-000000000011','$saleCrab')",
        "INSERT INTO be.`"SalesOrderLines`" (`"Id`",`"CrabId`") VALUES ('30000000-0000-0000-0000-000000000012','$saleCrab')"
    )
    $saleJobs = $saleSql | ForEach-Object {
        Start-Job -ScriptBlock {
            param($Psql, $Port, $Sql)
            & $Psql -X -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port `
                -U postgres -d postgres -c $Sql *> $null
            $LASTEXITCODE
        } -ArgumentList $psql, $Port, $_
    }
    $saleExit = $saleJobs | Wait-Job | Receive-Job
    $saleJobs | Remove-Job
    Assert-Equal 1 (Invoke-Psql "SELECT COUNT(*) FROM be.`"SalesOrderLines`" WHERE `"CrabId`"='$saleCrab'").Output `
        "The same crab was reserved by two active sales lines."
    Assert-Equal 1 (@($saleExit | Where-Object { $_ -ne 0 }).Count) `
        "Exactly one duplicate sale must be rejected."

    $syncBox = "40000000-0000-0000-0000-000000000001"
    Invoke-Psql "INSERT INTO be.`"SyncBoxes`" VALUES ('$syncBox','empty')" | Out-Null
    Invoke-Psql @"
BEGIN;
UPDATE be."SyncBoxes" SET "Status"='occupied' WHERE "Id"='$syncBox';
INSERT INTO be."SyncCrabs" VALUES ('40000000-0000-0000-0000-000000000011','DUP');
INSERT INTO be."SyncCrabs" VALUES ('40000000-0000-0000-0000-000000000012','DUP');
COMMIT;
"@ -AllowFailure | Out-Null
    Assert-Equal "empty" (Invoke-Psql "SELECT `"Status`" FROM be.`"SyncBoxes`" WHERE `"Id`"='$syncBox'").Output `
        "Failed sync operation changed box state."
    Assert-Equal 0 (Invoke-Psql "SELECT COUNT(*) FROM be.`"SyncCrabs`"").Output `
        "Failed sync operation left a crab row."

    $syncActor = "41000000-0000-0000-0000-000000000001"
    $otherActor = "41000000-0000-0000-0000-000000000002"
    $syncKey = "same-mobile-operation"
    $syncReservationSql = @(
        @"
BEGIN;
INSERT INTO be."SyncInboxItems" ("Id","ActorUserId","IdempotencyKey","RequestHash","Status")
VALUES ('41000000-0000-0000-0000-000000000011','$syncActor','$syncKey','HASH-A','pending');
SELECT pg_sleep(0.2);
INSERT INTO be."SyncCrabs" VALUES ('41000000-0000-0000-0000-000000000021','IDEMPOTENT-1');
UPDATE be."SyncInboxItems" SET "Status"='processed'
WHERE "ActorUserId"='$syncActor' AND "IdempotencyKey"='$syncKey';
COMMIT;
"@,
        @"
BEGIN;
INSERT INTO be."SyncInboxItems" ("Id","ActorUserId","IdempotencyKey","RequestHash","Status")
VALUES ('41000000-0000-0000-0000-000000000012','$syncActor','$syncKey','HASH-A','pending');
SELECT pg_sleep(0.2);
INSERT INTO be."SyncCrabs" VALUES ('41000000-0000-0000-0000-000000000022','IDEMPOTENT-2');
UPDATE be."SyncInboxItems" SET "Status"='processed'
WHERE "ActorUserId"='$syncActor' AND "IdempotencyKey"='$syncKey';
COMMIT;
"@
    )
    $syncReservationJobs = $syncReservationSql | ForEach-Object {
        Start-Job -ScriptBlock {
            param($Psql, $Port, $Sql)
            & $Psql -X -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $Port `
                -U postgres -d postgres -c $Sql *> $null
            $LASTEXITCODE
        } -ArgumentList $psql, $Port, $_
    }
    $syncReservationExit = $syncReservationJobs | Wait-Job | Receive-Job
    $syncReservationJobs | Remove-Job
    Assert-Equal 1 (Invoke-Psql "SELECT COUNT(*) FROM be.`"SyncInboxItems`" WHERE `"ActorUserId`"='$syncActor' AND `"IdempotencyKey`"='$syncKey'").Output `
        "Concurrent retries created duplicate inbox reservations."
    Assert-Equal 1 (Invoke-Psql "SELECT COUNT(*) FROM be.`"SyncCrabs`" WHERE `"Code`" LIKE 'IDEMPOTENT-%'").Output `
        "Concurrent retries executed the business mutation more than once."
    Assert-Equal 1 (@($syncReservationExit | Where-Object { $_ -ne 0 }).Count) `
        "Exactly one concurrent idempotency reservation must be rejected."
    Invoke-Psql "INSERT INTO be.`"SyncInboxItems`" (`"Id`",`"ActorUserId`",`"IdempotencyKey`",`"RequestHash`",`"Status`") VALUES ('41000000-0000-0000-0000-000000000013','$otherActor','$syncKey','HASH-B','processed')" | Out-Null
    Assert-Equal 2 (Invoke-Psql "SELECT COUNT(*) FROM be.`"SyncInboxItems`" WHERE `"IdempotencyKey`"='$syncKey'").Output `
        "The same client key must be independently scoped to different actors."

    Invoke-Psql @'
CREATE TABLE be."EdgeCommands" (
  "Id" uuid PRIMARY KEY,
  "Command" text NOT NULL
);
INSERT INTO be."EdgeCommands" VALUES
('50000000-0000-0000-0000-000000000001','off');
ALTER TABLE be."EdgeCommands"
ADD COLUMN "RequestSource" text NOT NULL DEFAULT 'legacy';
'@ | Out-Null
    Assert-Equal "legacy" (Invoke-Psql "SELECT `"RequestSource`" FROM be.`"EdgeCommands`"").Output `
        "Hotfix migration did not preserve/backfill the legacy command."

    Write-Output "POSTGRES_HOTFIX_TESTS_PASSED=6"
}
finally {
    & $pgctl -D $data stop -m fast -w
}
