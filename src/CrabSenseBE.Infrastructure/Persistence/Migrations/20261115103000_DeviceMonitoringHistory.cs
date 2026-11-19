using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261115103000_DeviceMonitoringHistory")]
public sealed class DeviceMonitoringHistory:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
 CREATE TABLE be."DeviceConnectionEvents" ("Id" uuid PRIMARY KEY,"CreatedAt" timestamptz NOT NULL,"UpdatedAt" timestamptz NULL,
 "SourceType" text NOT NULL,"SourceId" uuid NOT NULL,"FarmingAreaId" uuid NULL,"PreviousStatus" text NULL,"NewStatus" text NOT NULL,
 "ReasonCode" text NOT NULL,"OccurredAt" timestamptz NOT NULL,"LastSeenAt" timestamptz NULL);
 CREATE INDEX "IX_DeviceConnectionEvents_SourceType_SourceId_OccurredAt" ON be."DeviceConnectionEvents"("SourceType","SourceId","OccurredAt");
 CREATE INDEX "IX_DeviceConnectionEvents_OccurredAt" ON be."DeviceConnectionEvents"("OccurredAt");
 CREATE TABLE be."DeviceMonitoringStates" ("Id" uuid PRIMARY KEY,"CreatedAt" timestamptz NOT NULL,"UpdatedAt" timestamptz NULL,
 "SourceType" text NOT NULL,"SourceId" uuid NOT NULL,"FarmingAreaId" uuid NULL,"Status" text NOT NULL,"ObservedAt" timestamptz NOT NULL);
 CREATE UNIQUE INDEX "IX_DeviceMonitoringStates_SourceType_SourceId" ON be."DeviceMonitoringStates"("SourceType","SourceId");
 """);
 protected override void Down(MigrationBuilder m)=>m.Sql("""DROP TABLE be."DeviceMonitoringStates"; DROP TABLE be."DeviceConnectionEvents";""");
}
