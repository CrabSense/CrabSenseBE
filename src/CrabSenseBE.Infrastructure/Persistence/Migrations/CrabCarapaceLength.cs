using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>Cua: bề ngang mai (CarapaceLengthMm) cạnh bề rộng (CarapaceWidthMm).</summary>
public partial class CrabCarapaceLength : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."Crabs"
            ADD COLUMN IF NOT EXISTS "CarapaceLengthMm" numeric(8,2) NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."Crabs"
            DROP COLUMN IF EXISTS "CarapaceLengthMm";
            """);
    }
}
