using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261126100000_WaterBenchConfiguration")]
public sealed class AddWaterBenchConfiguration : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE be."WaterBenchStations"
            ADD COLUMN "ConfigurationVersion" integer NOT NULL DEFAULT 0,
            ADD COLUMN "ScheduleEnabled" boolean NOT NULL DEFAULT false,
            ADD COLUMN "DailyTimesJson" text NOT NULL DEFAULT '[]',
            ADD COLUMN "GraceMinutes" integer NOT NULL DEFAULT 5,
            ADD COLUMN "TargetsJson" text NOT NULL DEFAULT '[]',
            ADD COLUMN "SampleSource" varchar(32) NOT NULL DEFAULT '',
            ADD CONSTRAINT "CK_WaterBenchStation_Config" CHECK ("ConfigurationVersion" >= 0 AND "GraceMinutes" BETWEEN 1 AND 15 AND jsonb_typeof("DailyTimesJson"::jsonb) = 'array' AND jsonb_typeof("TargetsJson"::jsonb) = 'array');
        -- Internal constraint table, intentionally not an application entity.
        CREATE TABLE be."WaterBenchHardwareClaims" (
            "HardwareId" varchar(12) PRIMARY KEY,
            "StationId" uuid NOT NULL REFERENCES be."WaterBenchStations"("Id") ON DELETE RESTRICT
        );
        INSERT INTO be."WaterBenchHardwareClaims" ("HardwareId","StationId")
            SELECT "MasterHardwareId","Id" FROM be."WaterBenchStations" WHERE "RevokedAt" IS NULL
            UNION ALL SELECT "SlaveHardwareId","Id" FROM be."WaterBenchStations" WHERE "RevokedAt" IS NULL;
        CREATE FUNCTION be.water_bench_claim_hardware() RETURNS trigger LANGUAGE plpgsql AS $fn$
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
        END $fn$;
        CREATE TRIGGER water_bench_claim_hardware AFTER INSERT OR UPDATE ON be."WaterBenchStations"
            FOR EACH ROW EXECUTE FUNCTION be.water_bench_claim_hardware();
        CREATE FUNCTION be.water_bench_keep_area() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
          IF NEW."FarmingAreaId" IS DISTINCT FROM OLD."FarmingAreaId" AND EXISTS (
            SELECT 1 FROM be."WaterBenchStations" WHERE "KioskId"=OLD."Id") THEN
            RAISE EXCEPTION 'Kiosk with water bench history cannot move area' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $fn$;
        CREATE TRIGGER water_bench_keep_area BEFORE UPDATE OF "FarmingAreaId" ON be."FarmKiosks"
            FOR EACH ROW EXECUTE FUNCTION be.water_bench_keep_area();
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""
        DROP TRIGGER water_bench_keep_area ON be."FarmKiosks";
        DROP FUNCTION be.water_bench_keep_area();
        DROP TRIGGER water_bench_claim_hardware ON be."WaterBenchStations";
        DROP FUNCTION be.water_bench_claim_hardware();
        DROP TABLE be."WaterBenchHardwareClaims";
        ALTER TABLE be."WaterBenchStations" DROP CONSTRAINT "CK_WaterBenchStation_Config",
            DROP COLUMN "ConfigurationVersion", DROP COLUMN "ScheduleEnabled", DROP COLUMN "DailyTimesJson",
            DROP COLUMN "GraceMinutes", DROP COLUMN "TargetsJson", DROP COLUMN "SampleSource";
        """);
}
