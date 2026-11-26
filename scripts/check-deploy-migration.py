#!/usr/bin/env python3
"""Conservative deployment guard; inspect generated UP SQL, never rewrite executed SQL."""
import re
import sys
from pathlib import Path

INCIDENT_MIGRATION = "20261113123000_RepairAlertIncidentIdText"
BENCH_MIGRATION = "20261126100000_WaterBenchConfiguration"
INCIDENT_STATEMENT = 'ALTER TABLE be."Alerts" ALTER COLUMN "IncidentId" TYPE text;'
BENCH_DELETE = 'DELETE FROM be."WaterBenchHardwareClaims" WHERE "StationId"=NEW."Id";'
# Reviewed trigger definition from the immutable migration. Ignore indentation only.
# This DELETE runs on revocation of a single station, not while applying the migration.
BENCH_FUNCTION = """CREATE FUNCTION be.water_bench_claim_hardware() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
IF TG_OP = 'UPDATE' THEN
IF NEW."KioskId" <> OLD."KioskId" OR NEW."MasterHardwareId" <> OLD."MasterHardwareId" OR NEW."SlaveHardwareId" <> OLD."SlaveHardwareId" THEN
RAISE EXCEPTION 'Water bench identity is immutable' USING ERRCODE='23514';
END IF;
IF OLD."RevokedAt" IS NOT NULL AND NEW."RevokedAt" IS DISTINCT FROM OLD."RevokedAt" THEN
RAISE EXCEPTION 'Revoked water bench cannot be reactivated' USING ERRCODE='23514';
END IF;
IF OLD."RevokedAt" IS NULL AND NEW."RevokedAt" IS NOT NULL THEN
DELETE FROM be."WaterBenchHardwareClaims" WHERE "StationId"=NEW."Id";
END IF;
ELSIF NEW."RevokedAt" IS NULL THEN
INSERT INTO be."WaterBenchHardwareClaims" ("HardwareId","StationId")
SELECT x,NEW."Id" FROM unnest(ARRAY[NEW."MasterHardwareId",NEW."SlaveHardwareId"]) AS x ORDER BY x;
END IF;
RETURN NEW;
END $fn$;""".splitlines()

DESTRUCTIVE = re.compile(
    r"\b(?:DROP\s+(?:TABLE|COLUMN)|RENAME\s+(?:TABLE|COLUMN)|ALTER\s+COLUMN|TRUNCATE|DELETE\s+FROM)\b",
    re.IGNORECASE,
)


def check(migration: str, sql: str) -> None:
    lines = sql.splitlines()
    normalized = [line.strip() for line in lines]
    excluded = set()
    if migration == INCIDENT_MIGRATION:
        matches = [i for i, line in enumerate(normalized) if line == INCIDENT_STATEMENT]
        if len(matches) == 1:
            excluded.add(matches[0])
    if migration == BENCH_MIGRATION:
        starts = [
            i for i in range(len(lines) - len(BENCH_FUNCTION) + 1)
            if normalized[i:i + len(BENCH_FUNCTION)] == BENCH_FUNCTION
        ]
        # Only the single DELETE in the exact reviewed function is exempt.
        # Any change in condition, function body, table or predicate is rejected.
        if len(starts) == 1 and BENCH_FUNCTION.count(BENCH_DELETE) == 1:
            excluded.add(starts[0] + BENCH_FUNCTION.index(BENCH_DELETE))
    checked = "\n".join(line if i not in excluded else "" for i, line in enumerate(lines))
    if DESTRUCTIVE.search(checked):
        raise ValueError("Latest migration is destructive and cannot use app-only rollback.")


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: check-deploy-migration.py MIGRATION_ID SQL_FILE", file=sys.stderr)
        return 2
    try:
        sql = Path(sys.argv[2]).read_text(encoding="utf-8-sig")
        if not sql.strip():
            raise ValueError("Generated migration SQL is empty.")
        check(sys.argv[1], sql)
    except (OSError, UnicodeError, ValueError) as exc:
        print(str(exc), file=sys.stderr)
        return 1
    print("Latest migration deployment guard passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
