using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261114200000_CameraRecordingPolicies")]
public sealed class CameraRecordingPolicies : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
ALTER TABLE be."CameraSchedules" ADD COLUMN "AutoDeleteExpired" boolean NOT NULL DEFAULT false;
ALTER TABLE be."CameraRecordingExecutions" DROP CONSTRAINT "CK_CameraRecordingExecutions_Status";
ALTER TABLE be."CameraRecordingExecutions" ADD CONSTRAINT "CK_CameraRecordingExecutions_Status" CHECK ("Status" IN ('Recording','Uploading','Completed','Failed','Stopped') AND ("Status" = 'Recording') = ("EndedAt" IS NULL));
""");
    protected override void Down(MigrationBuilder m) => m.Sql("""
UPDATE be."CameraRecordingExecutions" SET "Status" = 'Failed', "Error" = 'Upload interrupted by schema rollback.' WHERE "Status" = 'Uploading';
ALTER TABLE be."CameraRecordingExecutions" DROP CONSTRAINT "CK_CameraRecordingExecutions_Status";
ALTER TABLE be."CameraRecordingExecutions" ADD CONSTRAINT "CK_CameraRecordingExecutions_Status" CHECK ("Status" IN ('Recording','Completed','Failed','Stopped') AND ("Status" = 'Recording') = ("EndedAt" IS NULL));
ALTER TABLE be."CameraSchedules" DROP COLUMN "AutoDeleteExpired";
""");
}
