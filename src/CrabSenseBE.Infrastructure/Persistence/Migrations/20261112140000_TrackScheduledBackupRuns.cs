using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    public partial class TrackScheduledBackupRuns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>("LastAttemptSlotUtc", "BackupScheduleSettings", schema: "be", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>("LastCompletedAtUtc", "BackupScheduleSettings", schema: "be", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<string>("LastResult", "BackupScheduleSettings", schema: "be", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>("LastBackupFile", "BackupScheduleSettings", schema: "be", type: "text", nullable: true);
            // Old API did not run a scheduler; require a new explicit enablement.
            migrationBuilder.Sql("UPDATE be.\"BackupScheduleSettings\" SET \"Enabled\" = false WHERE \"Enabled\" = true;");
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM be."BackupScheduleSettings" WHERE "LastAttemptSlotUtc" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot discard backup execution history.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn("LastAttemptSlotUtc", "BackupScheduleSettings", schema: "be");
            migrationBuilder.DropColumn("LastCompletedAtUtc", "BackupScheduleSettings", schema: "be");
            migrationBuilder.DropColumn("LastResult", "BackupScheduleSettings", schema: "be");
            migrationBuilder.DropColumn("LastBackupFile", "BackupScheduleSettings", schema: "be");
        }
    }
}
