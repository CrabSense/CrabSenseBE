using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113143100_EdgeCommandInventoryRevision")]
public sealed class EdgeCommandInventoryRevisionMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<long>(name: "InventoryRevision", schema: "be", table: "EdgeCommands", type: "bigint", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "InventoryRevision", schema: "be", table: "EdgeCommands");
}
