using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261114193000_CameraSchedules")]
public sealed class CameraSchedulesMigration : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
CREATE TABLE be."CameraSchedules" ("Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
"Name" text NOT NULL,
"Mode" text NOT NULL,
"Enabled" boolean NOT NULL,
"Timezone" text NOT NULL,
"RetentionDays" integer NOT NULL,
"VideoQuality" text NULL,
"Fps" integer NULL,
"MaxStorageGb" integer NULL,
"Version" integer NOT NULL,
"Deleted" boolean NOT NULL);

CREATE TABLE be."CameraScheduleAssignments" ("Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
"ScheduleId" uuid NOT NULL,
"DeviceId" uuid NOT NULL,
FOREIGN KEY ("ScheduleId") REFERENCES be."CameraSchedules" ("Id") ON DELETE CASCADE,
FOREIGN KEY ("DeviceId") REFERENCES be."Devices" ("Id") ON DELETE RESTRICT);
CREATE UNIQUE INDEX "IX_CameraScheduleAssignments_DeviceId" ON be."CameraScheduleAssignments" ("DeviceId");
CREATE INDEX "IX_CameraScheduleAssignments_ScheduleId" ON be."CameraScheduleAssignments" ("ScheduleId");
CREATE TABLE be."CameraScheduleSlots" ("Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
"ScheduleId" uuid NOT NULL,
"Day" integer NOT NULL,
"StartMinute" integer NOT NULL,
"EndMinute" integer NOT NULL,
FOREIGN KEY ("ScheduleId") REFERENCES be."CameraSchedules" ("Id") ON DELETE CASCADE);
CREATE INDEX "IX_CameraScheduleSlots_ScheduleId" ON be."CameraScheduleSlots" ("ScheduleId");
CREATE TABLE be."CameraRecordingExecutions" ("Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
"ScheduleId" uuid NOT NULL,
"DeviceId" uuid NOT NULL,
"ScheduleVersion" integer NOT NULL,
"Status" text NOT NULL,
"StartedAt" timestamp with time zone NOT NULL,
"LastHeartbeatAt" timestamp with time zone NOT NULL,
"EndedAt" timestamp with time zone NULL,
"Error" text NULL,
"MediaAssetId" uuid NULL,
FOREIGN KEY ("ScheduleId") REFERENCES be."CameraSchedules" ("Id") ON DELETE RESTRICT,
FOREIGN KEY ("DeviceId") REFERENCES be."Devices" ("Id") ON DELETE RESTRICT,
FOREIGN KEY ("MediaAssetId") REFERENCES be."MediaAssets" ("Id") ON DELETE SET NULL);
CREATE INDEX "IX_CameraRecordingExecutions_ScheduleId" ON be."CameraRecordingExecutions" ("ScheduleId");
CREATE INDEX "IX_CameraRecordingExecutions_DeviceId" ON be."CameraRecordingExecutions" ("DeviceId");
CREATE INDEX "IX_CameraRecordingExecutions_MediaAssetId" ON be."CameraRecordingExecutions" ("MediaAssetId");
""");
    protected override void Down(MigrationBuilder m) => m.Sql("""
DROP TABLE be."CameraRecordingExecutions";
DROP TABLE be."CameraScheduleSlots";
DROP TABLE be."CameraScheduleAssignments";
DROP TABLE be."CameraSchedules";
""");
}
