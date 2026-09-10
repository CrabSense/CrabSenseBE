using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>Khu: Location + mã AREA-A01 (backfill FARM-xxx / mã rỗng).</summary>
public partial class FarmingAreaKhuLocation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Location may already exist from EnsureFarmingAreaProfileSchemaAsync.
        // Schema only — AREA-xxx backfill lives in DevDbBootstrap (CHR needs ::int).
        migrationBuilder.Sql(
            """
            ALTER TABLE be."FarmingAreas"
            ADD COLUMN IF NOT EXISTS "Location" character varying(256);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Location",
            schema: "be",
            table: "FarmingAreas");
    }
}
