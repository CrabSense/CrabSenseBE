using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261114194000_CameraScheduleIntegrity")]
public sealed class CameraScheduleIntegrity : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
CREATE UNIQUE INDEX "IX_CameraRecordingExecutions_ActiveDevice" ON be."CameraRecordingExecutions" ("DeviceId") WHERE "Status" = 'Recording';
ALTER TABLE be."CameraScheduleSlots" ADD CONSTRAINT "CK_CameraScheduleSlots_Range" CHECK ("Day" BETWEEN 1 AND 7 AND "StartMinute" >= 0 AND "EndMinute" <= 1440 AND "StartMinute" < "EndMinute");
ALTER TABLE be."CameraSchedules" ADD CONSTRAINT "CK_CameraSchedules_Config" CHECK ("Mode" IN ('Continuous','Scheduled') AND "Version" >= 1 AND "RetentionDays" BETWEEN 1 AND 3650 AND ("Fps" IS NULL OR "Fps" BETWEEN 1 AND 120) AND ("MaxStorageGb" IS NULL OR "MaxStorageGb" >= 1));
ALTER TABLE be."CameraRecordingExecutions" ADD CONSTRAINT "CK_CameraRecordingExecutions_Status" CHECK ("Status" IN ('Recording','Completed','Failed','Stopped') AND ("Status" = 'Recording') = ("EndedAt" IS NULL));
""");
    protected override void Down(MigrationBuilder m) => m.Sql("""
ALTER TABLE be."CameraRecordingExecutions" DROP CONSTRAINT "CK_CameraRecordingExecutions_Status";
ALTER TABLE be."CameraSchedules" DROP CONSTRAINT "CK_CameraSchedules_Config";
ALTER TABLE be."CameraScheduleSlots" DROP CONSTRAINT "CK_CameraScheduleSlots_Range";
DROP INDEX be."IX_CameraRecordingExecutions_ActiveDevice";
""");
}
