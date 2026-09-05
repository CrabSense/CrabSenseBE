using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>Dãy: Code DAY-A01, Location, Description, Status, SortOrder.</summary>
public partial class FarmingRowKhuFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Columns may already exist from EnsureFarmingRowProfileSchemaAsync.
        migrationBuilder.Sql(
            """
            ALTER TABLE be."FarmingRows"
            ADD COLUMN IF NOT EXISTS "Code" character varying(32) NOT NULL DEFAULT '',
            ADD COLUMN IF NOT EXISTS "Location" character varying(256) NULL,
            ADD COLUMN IF NOT EXISTS "Description" text NULL,
            ADD COLUMN IF NOT EXISTS "Status" text NOT NULL DEFAULT 'Active',
            ADD COLUMN IF NOT EXISTS "SortOrder" integer NOT NULL DEFAULT 0;
            """);

        // DAY-xxx backfill lives in DevDbBootstrap (CHR needs ::int).
        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FarmingRows_Code"
            ON be."FarmingRows" ("Code");
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_FarmingRows_Code",
            schema: "be",
            table: "FarmingRows");

        migrationBuilder.DropColumn(name: "Code", schema: "be", table: "FarmingRows");
        migrationBuilder.DropColumn(name: "Location", schema: "be", table: "FarmingRows");
        migrationBuilder.DropColumn(name: "Description", schema: "be", table: "FarmingRows");
        migrationBuilder.DropColumn(name: "Status", schema: "be", table: "FarmingRows");
        migrationBuilder.DropColumn(name: "SortOrder", schema: "be", table: "FarmingRows");
    }
}
