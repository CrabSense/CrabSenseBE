using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113143200_ControllerPortObservations")]
public sealed class ControllerPortObservationsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "ObservedOn", schema: "be", table: "ControllerPorts", type: "boolean", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "LastObservedAt", schema: "be", table: "ControllerPorts", type: "timestamp with time zone", nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ObservedOn", schema: "be", table: "ControllerPorts");
        migrationBuilder.DropColumn(name: "LastObservedAt", schema: "be", table: "ControllerPorts");
    }
}
