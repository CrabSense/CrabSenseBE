using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125180000_AddMissingCrabLotSnapshot")]
public sealed class AddMissingCrabLotSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "MissingCountAtClose", schema: "be", table: "CrabLots",
            type: "integer", nullable: true);
        // The old closed snapshot already partitions registered crabs into
        // living, dead, harvested/sold and missing. Use only complete,
        // non-negative frozen snapshots; never use today's crab state.
        migrationBuilder.Sql("""
            UPDATE be."CrabLots"
            SET "MissingCountAtClose" = "RegisteredCountAtClose" - "AliveCountAtClose"
                - "DeadCountAtClose" - "HarvestedCountAtClose"
            WHERE "ClosedAt" IS NOT NULL
              AND "RegisteredCountAtClose" >= 0 AND "AliveCountAtClose" >= 0
              AND "DeadCountAtClose" >= 0 AND "HarvestedCountAtClose" >= 0
              AND "RegisteredCountAtClose" >= "AliveCountAtClose" + "DeadCountAtClose" + "HarvestedCountAtClose";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn("MissingCountAtClose", "CrabLots", schema: "be");
}
