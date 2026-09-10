using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>Trạng thái phiếu nhập: Pending / Allocating / Completed / Cancelled.</summary>
public partial class CrabLotWorkflowStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."CrabLots"
            ADD COLUMN IF NOT EXISTS "Status" character varying(16) NOT NULL DEFAULT 'Pending';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."CrabLots"
            DROP COLUMN IF EXISTS "Status";
            """);
    }
}
