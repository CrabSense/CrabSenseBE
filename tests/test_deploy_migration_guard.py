import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("migration_guard", ROOT / "scripts/check-deploy-migration.py")
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)
MIGRATION = ROOT / "src/CrabSenseBE.Infrastructure/Persistence/Migrations/20261126100000_WaterBenchConfiguration.cs"


class MigrationGuardTests(unittest.TestCase):
    def setUp(self):
        # Use actual migration UP body, not a mocked copy of the trigger.
        self.up = MIGRATION.read_text(encoding="utf-8-sig").split('Up(MigrationBuilder m)=>m.Sql("""', 1)[1].split('""");', 1)[0]

    def test_reviewed_up_is_allowed(self):
        guard.check(guard.BENCH_MIGRATION, self.up)

    def test_old_incident_exception_remains_exact(self):
        guard.check(guard.INCIDENT_MIGRATION, guard.INCIDENT_STATEMENT)
        with self.assertRaises(ValueError):
            guard.check(guard.INCIDENT_MIGRATION, guard.INCIDENT_STATEMENT + '\nDROP TABLE be."Alerts";')

    def test_wrong_migration_is_rejected(self):
        with self.assertRaises(ValueError):
            guard.check("20261127000000_Other", self.up)

    def test_unrestricted_or_modified_delete_is_rejected(self):
        for replacement in ['DELETE FROM be."WaterBenchHardwareClaims";',
                            'DELETE FROM be."WaterBenchHardwareClaims" WHERE "StationId"=OLD."Id";',
                            'DELETE FROM be."WaterBenchEvidence" WHERE "StationId"=NEW."Id";']:
            with self.subTest(replacement=replacement), self.assertRaises(ValueError):
                guard.check(guard.BENCH_MIGRATION, self.up.replace(guard.BENCH_DELETE, replacement))

    def test_modified_revocation_condition_is_rejected(self):
        sql = self.up.replace('OLD."RevokedAt" IS NULL AND NEW."RevokedAt" IS NOT NULL', 'TRUE')
        with self.assertRaises(ValueError):
            guard.check(guard.BENCH_MIGRATION, sql)

    def test_extra_destructive_sql_is_rejected(self):
        for extra in [guard.BENCH_DELETE, 'DROP TABLE be."WaterBenchEvidence";',
                      'ALTER TABLE be."WaterBenchStations" DROP COLUMN "KioskId";',
                      'ALTER TABLE be."WaterBenchStations" ALTER COLUMN "KioskId" TYPE text;',
                      'TRUNCATE be."WaterBenchStations";', 'DELETE\n FROM be."WaterBenchStations";']:
            with self.subTest(extra=extra), self.assertRaises(ValueError):
                guard.check(guard.BENCH_MIGRATION, self.up + "\n" + extra)

    def test_duplicate_reviewed_function_is_rejected(self):
        with self.assertRaises(ValueError):
            guard.check(guard.BENCH_MIGRATION, self.up + "\n" + "\n".join(guard.BENCH_FUNCTION))

    def test_additive_sql_still_allowed(self):
        guard.check("Other", 'ALTER TABLE be."WaterBenchStations" ADD COLUMN "NewField" text;')


if __name__ == "__main__":
    unittest.main()
