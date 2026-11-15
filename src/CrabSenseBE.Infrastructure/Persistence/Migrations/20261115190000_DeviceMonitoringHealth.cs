using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261115190000_DeviceMonitoringHealth")]
public sealed class DeviceMonitoringHealth : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE be."DeviceMonitoringPolicies" ADD COLUMN "IsInherited" boolean NOT NULL DEFAULT false;
        ALTER TABLE be."DeviceMonitoringStates" ADD COLUMN "HealthStatus" text NULL, ADD COLUMN "HealthReason" text NULL, ADD COLUMN "AgentStatus" text NULL;
        CREATE TABLE be."DeviceHealthEvents" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "SourceType" text NOT NULL, "SourceId" uuid NOT NULL, "FarmingAreaId" uuid NULL,
          "OccurredAt" timestamptz NOT NULL, "PreviousHealth" text NULL, "HealthStatus" text NOT NULL,
          "ReasonCode" text NULL, "AgentStatus" text NULL
        );
        CREATE INDEX "IX_DeviceHealthEvents_SourceType_SourceId_OccurredAt" ON be."DeviceHealthEvents"("SourceType","SourceId","OccurredAt");
        CREATE INDEX "IX_DeviceHealthEvents_OccurredAt" ON be."DeviceHealthEvents"("OccurredAt");
        CREATE TABLE be."MonitoringWorkerStates" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "WorkerName" text NOT NULL, "LastSucceededAt" timestamptz NOT NULL, "DeviceCount" integer NOT NULL
        );
        CREATE UNIQUE INDEX "IX_MonitoringWorkerStates_WorkerName" ON be."MonitoringWorkerStates"("WorkerName");
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""
        DROP TABLE be."MonitoringWorkerStates";
        DROP TABLE be."DeviceHealthEvents";
        ALTER TABLE be."DeviceMonitoringStates" DROP COLUMN "HealthStatus", DROP COLUMN "HealthReason", DROP COLUMN "AgentStatus";
        DELETE FROM be."DeviceMonitoringPolicies" WHERE "IsInherited";
        ALTER TABLE be."DeviceMonitoringPolicies" DROP COLUMN "IsInherited";
        """);
}
