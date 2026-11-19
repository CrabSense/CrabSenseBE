using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261115231000_WaterAssayChannels")]
public sealed class WaterAssayChannelsMigration : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE be."WaterAssayChannels" (
            "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
            "BatchId" uuid NOT NULL REFERENCES be."WaterAnalysisBatches"("Id") ON DELETE RESTRICT,
            "CalibrationId" uuid NOT NULL REFERENCES be."WaterColorCalibrations"("Id") ON DELETE RESTRICT,
            "Analyte" text NOT NULL, "ProfileId" text NOT NULL, "ProfileJson" text NOT NULL, "ProtocolJson" text NOT NULL,
            "State" text NOT NULL, "StepIndex" integer NOT NULL, "StepStartedAt" timestamptz NOT NULL,
            "ReactionReadyAt" timestamptz NULL, "CapturedMediaId" uuid NULL REFERENCES be."MediaAssets"("Id") ON DELETE RESTRICT,
            "Failure" text NULL, "EventsJson" text NOT NULL);
        CREATE UNIQUE INDEX "IX_WaterAssayChannels_BatchId_Analyte" ON be."WaterAssayChannels"("BatchId","Analyte");
        CREATE INDEX "IX_WaterAssayChannels_CalibrationId" ON be."WaterAssayChannels"("CalibrationId");
        CREATE UNIQUE INDEX "IX_WaterAssayChannels_CapturedMediaId" ON be."WaterAssayChannels"("CapturedMediaId");
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""DROP TABLE be."WaterAssayChannels";""");
}
