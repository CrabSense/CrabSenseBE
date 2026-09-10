using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>Additive: Device as multi-ESP Controller (Name, MAC, IP, FarmingAreaId).</summary>
public partial class DeviceControllerFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."Devices"
            ADD COLUMN IF NOT EXISTS "Name" text NULL,
            ADD COLUMN IF NOT EXISTS "MacAddress" character varying(64) NULL,
            ADD COLUMN IF NOT EXISTS "IpAddress" character varying(64) NULL,
            ADD COLUMN IF NOT EXISTS "FarmingAreaId" uuid NULL;

            CREATE INDEX IF NOT EXISTS "IX_Devices_FarmingAreaId"
                ON be."Devices" ("FarmingAreaId");
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."Devices"
            DROP CONSTRAINT IF EXISTS "FK_Devices_FarmingAreas_FarmingAreaId";
            DROP INDEX IF EXISTS be."IX_Devices_FarmingAreaId";
            """);
    }
}
