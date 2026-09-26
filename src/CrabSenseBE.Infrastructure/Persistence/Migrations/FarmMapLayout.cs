using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>
/// Bản đồ trại: ảnh nền theo khu (MapImageUrl), khung khu trên ảnh (MapX1..MapY2)
/// và tâm dãy/hộp trên ảnh (MapX, MapY). Tất cả là tỉ lệ 0–1 theo kích thước ảnh.
/// </summary>
public partial class FarmMapLayout : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."FarmingAreas"
            ADD COLUMN IF NOT EXISTS "MapImageUrl" text NULL,
            ADD COLUMN IF NOT EXISTS "MapX1" numeric(7,4) NULL,
            ADD COLUMN IF NOT EXISTS "MapY1" numeric(7,4) NULL,
            ADD COLUMN IF NOT EXISTS "MapX2" numeric(7,4) NULL,
            ADD COLUMN IF NOT EXISTS "MapY2" numeric(7,4) NULL;

            ALTER TABLE be."FarmingRows"
            ADD COLUMN IF NOT EXISTS "MapX" numeric(7,4) NULL,
            ADD COLUMN IF NOT EXISTS "MapY" numeric(7,4) NULL;

            ALTER TABLE be."Boxes"
            ADD COLUMN IF NOT EXISTS "MapX" numeric(7,4) NULL,
            ADD COLUMN IF NOT EXISTS "MapY" numeric(7,4) NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."Boxes"
            DROP COLUMN IF EXISTS "MapX",
            DROP COLUMN IF EXISTS "MapY";

            ALTER TABLE be."FarmingRows"
            DROP COLUMN IF EXISTS "MapX",
            DROP COLUMN IF EXISTS "MapY";

            ALTER TABLE be."FarmingAreas"
            DROP COLUMN IF EXISTS "MapImageUrl",
            DROP COLUMN IF EXISTS "MapX1",
            DROP COLUMN IF EXISTS "MapY1",
            DROP COLUMN IF EXISTS "MapX2",
            DROP COLUMN IF EXISTS "MapY2";
            """);
    }
}
