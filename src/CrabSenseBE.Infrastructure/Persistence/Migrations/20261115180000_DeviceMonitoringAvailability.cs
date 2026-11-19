using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261115180000_DeviceMonitoringAvailability")]
public sealed class DeviceMonitoringAvailability : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        ALTER TABLE be."DeviceConnectionEvents" ADD COLUMN "PreviousObservedAt" timestamptz NULL;
        CREATE INDEX "IX_DeviceConnectionEvents_FarmingAreaId_OccurredAt"
            ON be."DeviceConnectionEvents"("FarmingAreaId","OccurredAt");
        """);
    protected override void Down(MigrationBuilder migration) => migration.Sql("""
        DROP INDEX be."IX_DeviceConnectionEvents_FarmingAreaId_OccurredAt";
        ALTER TABLE be."DeviceConnectionEvents" DROP COLUMN "PreviousObservedAt";
        """);
}
