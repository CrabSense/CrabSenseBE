using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261115104500_DeviceMonitoringPolicies")]
public sealed class DeviceMonitoringPolicies:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
 ALTER TABLE be."DeviceConnectionEvents" ADD COLUMN "PreviousObservedAt" timestamptz NULL;
 CREATE TABLE be."DeviceMonitoringPolicies" ("Id" uuid PRIMARY KEY,"CreatedAt" timestamptz NOT NULL,"UpdatedAt" timestamptz NULL,
 "SourceType" text NOT NULL CHECK ("SourceType" IN ('camera','controller','kiosk')),"HeartbeatTimeoutSeconds" integer NOT NULL CHECK ("HeartbeatTimeoutSeconds" BETWEEN 60 AND 86400),
 "Version" integer NOT NULL CHECK ("Version">0),"UpdatedBy" uuid NOT NULL);
 CREATE UNIQUE INDEX "IX_DeviceMonitoringPolicies_SourceType" ON be."DeviceMonitoringPolicies"("SourceType");
 """);
 protected override void Down(MigrationBuilder m)=>m.Sql("""DROP TABLE be."DeviceMonitoringPolicies"; ALTER TABLE be."DeviceConnectionEvents" DROP COLUMN "PreviousObservedAt";""");
}