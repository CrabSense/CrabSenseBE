using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

// The historical DeviceControllerFields file lacks migration discovery metadata.
// Repair at the current tip, not by inserting an old migration into deployed histories.
[DbContext(typeof(AppDbContext))]
[Migration("20261126120000_RepairDeviceAreaSchema")]
public sealed class RepairDeviceAreaSchema : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE be."Devices"
          ADD COLUMN IF NOT EXISTS "Name" text NULL,
          ADD COLUMN IF NOT EXISTS "MacAddress" character varying(64) NULL,
          ADD COLUMN IF NOT EXISTS "IpAddress" character varying(64) NULL,
          ADD COLUMN IF NOT EXISTS "FarmingAreaId" uuid NULL;

        CREATE INDEX IF NOT EXISTS "IX_Devices_FarmingAreaId" ON be."Devices" ("FarmingAreaId");

        DO $repair$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM be."Devices" d
                LEFT JOIN be."FarmingAreas" a ON a."Id"=d."FarmingAreaId"
                WHERE d."FarmingAreaId" IS NOT NULL AND a."Id" IS NULL
            ) THEN
                RAISE EXCEPTION 'Device area references require administrative review before migration';
            END IF;
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conrelid='be."Devices"'::regclass AND conname='FK_Devices_FarmingAreas_FarmingAreaId'
            ) THEN
                ALTER TABLE be."Devices" ADD CONSTRAINT "FK_Devices_FarmingAreas_FarmingAreaId"
                    FOREIGN KEY ("FarmingAreaId") REFERENCES be."FarmingAreas" ("Id") ON DELETE SET NULL;
            END IF;
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint c
                WHERE c.conrelid='be."Devices"'::regclass
                  AND c.conname='FK_Devices_FarmingAreas_FarmingAreaId'
                  AND c.contype='f' AND c.confrelid='be."FarmingAreas"'::regclass
                  AND c.confdeltype='n'
                  AND c.conkey=ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid=c.conrelid AND attname='FarmingAreaId')]::smallint[]
                  AND c.confkey=ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid=c.confrelid AND attname='Id')]::smallint[]
            ) THEN
                RAISE EXCEPTION 'Existing device area constraint differs from the EF model; review before migration';
            END IF;
        END $repair$;
        ALTER TABLE be."Devices" VALIDATE CONSTRAINT "FK_Devices_FarmingAreas_FarmingAreaId";
        """);

    // Preserve fields and assignments on rollback, including schemas that already had them.
    protected override void Down(MigrationBuilder m) { }
}
