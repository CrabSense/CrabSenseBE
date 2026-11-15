using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261115210000_DeviceMonitoringMaintenanceControllerHealth")]
public sealed class DeviceMonitoringMaintenanceControllerHealth : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE be."Devices" ADD COLUMN "ControllerCommandStorageReady" boolean NULL,
          ADD COLUMN "ControllerSensorReadSupported" boolean NULL, ADD COLUMN "ControllerRssiDbm" integer NULL,
          ADD COLUMN "ControllerFreeHeapBytes" bigint NULL, ADD COLUMN "ControllerUptimeSeconds" bigint NULL,
          ADD COLUMN "ControllerHealthAt" timestamptz NULL;
        CREATE TABLE be."DeviceMaintenanceWindows" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "SourceType" text NOT NULL, "SourceId" uuid NOT NULL, "StartsAt" timestamptz NOT NULL,
          "EndsAt" timestamptz NOT NULL, "Reason" text NOT NULL, "CreatedBy" uuid NOT NULL,
          "CancelledAt" timestamptz NULL, "CancelledBy" uuid NULL,
          CONSTRAINT "CK_DeviceMaintenanceWindows_Time" CHECK ("EndsAt">"StartsAt")
        );
        CREATE INDEX "IX_DeviceMaintenanceWindows_SourceType_SourceId_StartsAt" ON be."DeviceMaintenanceWindows"("SourceType","SourceId","StartsAt");
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""
        DROP TABLE be."DeviceMaintenanceWindows";
        ALTER TABLE be."Devices" DROP COLUMN "ControllerCommandStorageReady", DROP COLUMN "ControllerSensorReadSupported",
          DROP COLUMN "ControllerRssiDbm", DROP COLUMN "ControllerFreeHeapBytes", DROP COLUMN "ControllerUptimeSeconds", DROP COLUMN "ControllerHealthAt";
        """);
}
