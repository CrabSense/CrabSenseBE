using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

public partial class ScheduledFarmTasks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS be."ScheduledFarmTasks" (
                "Id" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "OwnerId" uuid NOT NULL,
                "FarmingAreaId" uuid NULL,
                "Title" character varying(200) NOT NULL,
                "Description" character varying(1000) NULL,
                "RecurrenceType" character varying(16) NOT NULL,
                "DaysOfWeekJson" text NOT NULL,
                "StartDate" timestamp with time zone NOT NULL,
                "EndDate" timestamp with time zone NULL,
                "ReminderMinuteOfDay" integer NOT NULL,
                "IsEnabled" boolean NOT NULL,
                "NextRunAt" timestamp with time zone NULL,
                CONSTRAINT "PK_ScheduledFarmTasks" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_ScheduledFarmTasks_OwnerId"
                ON be."ScheduledFarmTasks" ("OwnerId");
            CREATE INDEX IF NOT EXISTS "IX_ScheduledFarmTasks_FarmingAreaId"
                ON be."ScheduledFarmTasks" ("FarmingAreaId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""DROP TABLE IF EXISTS be."ScheduledFarmTasks";""");
}
