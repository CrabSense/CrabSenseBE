using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113143000_ControllerPortInventory")]
public sealed class ControllerPortInventoryMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE be."ControllerPortInventories" (
                "DeviceId" uuid PRIMARY KEY REFERENCES be."Devices"("Id") ON DELETE CASCADE,
                "Revision" bigint NOT NULL,
                "ManifestHash" text NOT NULL,
                "ReportedAt" timestamp with time zone NOT NULL
            );
            CREATE TABLE be."ControllerPorts" (
                "DeviceId" uuid NOT NULL REFERENCES be."Devices"("Id") ON DELETE CASCADE,
                "PortId" character varying(64) NOT NULL,
                "Channel" text NULL,
                "Gpio" integer NULL,
                "Kind" text NOT NULL,
                "Direction" text NOT NULL,
                "Discovery" text NOT NULL,
                "HardwareUid" text NULL,
                "Present" boolean NOT NULL,
                "RemoteControl" boolean NOT NULL,
                "LastSeenAt" timestamp with time zone NOT NULL,
                PRIMARY KEY ("DeviceId", "PortId")
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE be."ControllerPorts";
            DROP TABLE be."ControllerPortInventories";
            """);
    }
}
