using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>
/// Chi tiết khu → Camera giám sát / Thông số môi trường:
/// - Devices: gắn camera theo dãy (FarmingRowId), URL stream/snapshot, độ phân giải.
/// - Sensors: gắn cảm biến theo dãy (FarmingRowId) để so sánh thông số giữa các dãy.
/// </summary>
public partial class CameraAndSensorRowBinding : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."Devices"
            ADD COLUMN IF NOT EXISTS "FarmingRowId" uuid NULL,
            ADD COLUMN IF NOT EXISTS "StreamUrl" text NULL,
            ADD COLUMN IF NOT EXISTS "SnapshotUrl" text NULL,
            ADD COLUMN IF NOT EXISTS "Resolution" character varying(32) NULL;

            CREATE INDEX IF NOT EXISTS "IX_Devices_FarmingRowId" ON be."Devices" ("FarmingRowId");

            ALTER TABLE be."Sensors"
            ADD COLUMN IF NOT EXISTS "FarmingRowId" uuid NULL;

            CREATE INDEX IF NOT EXISTS "IX_Sensors_FarmingRowId" ON be."Sensors" ("FarmingRowId");
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS be."IX_Sensors_FarmingRowId";
            ALTER TABLE be."Sensors" DROP COLUMN IF EXISTS "FarmingRowId";

            DROP INDEX IF EXISTS be."IX_Devices_FarmingRowId";
            ALTER TABLE be."Devices"
            DROP COLUMN IF EXISTS "FarmingRowId",
            DROP COLUMN IF EXISTS "StreamUrl",
            DROP COLUMN IF EXISTS "SnapshotUrl",
            DROP COLUMN IF EXISTS "Resolution";
            """);
    }
}
