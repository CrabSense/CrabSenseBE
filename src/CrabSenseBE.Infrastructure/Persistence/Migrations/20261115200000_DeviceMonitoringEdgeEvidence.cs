using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261115200000_DeviceMonitoringEdgeEvidence")]
public sealed class DeviceMonitoringEdgeEvidence : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE be."FarmKiosks" ADD COLUMN "MetricsReportedAt" timestamptz NULL,
          ADD COLUMN "ProcessCpuPercent" double precision NULL, ADD COLUMN "ProcessMemoryBytes" bigint NULL,
          ADD COLUMN "DiskTotalBytes" bigint NULL, ADD COLUMN "DiskFreeBytes" bigint NULL, ADD COLUMN "SoftwareVersion" text NULL,
          ADD COLUMN "EdgeSyncedAt" timestamptz NULL, ADD COLUMN "RecordingCheckedAt" timestamptz NULL, ADD COLUMN "UploadCheckedAt" timestamptz NULL;
        ALTER TABLE be."CameraRecordingAgents" ADD COLUMN "StreamProbeStatus" text NULL,
          ADD COLUMN "StreamProbedAt" timestamptz NULL, ADD COLUMN "LastFrameObservedAt" timestamptz NULL;
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE be."CameraRecordingAgents" DROP COLUMN "StreamProbeStatus", DROP COLUMN "StreamProbedAt", DROP COLUMN "LastFrameObservedAt";
        ALTER TABLE be."FarmKiosks" DROP COLUMN "MetricsReportedAt", DROP COLUMN "ProcessCpuPercent", DROP COLUMN "ProcessMemoryBytes",
          DROP COLUMN "DiskTotalBytes", DROP COLUMN "DiskFreeBytes", DROP COLUMN "SoftwareVersion", DROP COLUMN "EdgeSyncedAt", DROP COLUMN "RecordingCheckedAt", DROP COLUMN "UploadCheckedAt";
        """);
}
