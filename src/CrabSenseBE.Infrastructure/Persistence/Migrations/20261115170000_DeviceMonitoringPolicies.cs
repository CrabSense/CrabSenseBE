using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261115170000_DeviceMonitoringPolicies")]
public sealed class DeviceMonitoringPolicies : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE be."DeviceMonitoringPolicies" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "SourceType" text NOT NULL, "SourceId" uuid NOT NULL, "TimeoutSeconds" integer NOT NULL,
          "Version" integer NOT NULL, "UpdatedBy" uuid NOT NULL,
          CONSTRAINT "CK_DeviceMonitoringPolicies_Type" CHECK ("SourceType" IN ('camera','controller','kiosk','agent')),
          CONSTRAINT "CK_DeviceMonitoringPolicies_Timeout" CHECK ("TimeoutSeconds" BETWEEN 30 AND 86400)
        );
        CREATE UNIQUE INDEX "IX_DeviceMonitoringPolicies_SourceType_SourceId"
          ON be."DeviceMonitoringPolicies"("SourceType","SourceId");
        """);
    protected override void Down(MigrationBuilder m) => m.Sql("""DROP TABLE be."DeviceMonitoringPolicies";""");
}
