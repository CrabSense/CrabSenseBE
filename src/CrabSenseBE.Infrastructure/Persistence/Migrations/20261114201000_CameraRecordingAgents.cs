using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261114201000_CameraRecordingAgents")]
public sealed class CameraRecordingAgents : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
CREATE TABLE be."CameraRecordingAgents" (
 "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
 "DeviceId" uuid NOT NULL REFERENCES be."Devices"("Id") ON DELETE RESTRICT,
 "KioskId" uuid NULL REFERENCES be."FarmKiosks"("Id") ON DELETE SET NULL,
 "Enabled" boolean NOT NULL, "Version" integer NOT NULL CHECK ("Version">0),
 "LastSeenAt" timestamptz NULL, "WorkerVersion" varchar(80) NULL,
 "PendingClips" integer NOT NULL CHECK ("PendingClips" BETWEEN 0 AND 1000));
CREATE UNIQUE INDEX "IX_CameraRecordingAgents_DeviceId" ON be."CameraRecordingAgents"("DeviceId");
CREATE INDEX "IX_CameraRecordingAgents_KioskId" ON be."CameraRecordingAgents"("KioskId");
""");
    protected override void Down(MigrationBuilder m) => m.Sql("""DROP TABLE be."CameraRecordingAgents";""");
}
