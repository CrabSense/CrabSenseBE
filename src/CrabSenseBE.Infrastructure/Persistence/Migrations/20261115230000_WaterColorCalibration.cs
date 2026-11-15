using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261115230000_WaterColorCalibration")]
public sealed class WaterColorCalibrationMigration : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE be."WaterColorCalibrations" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "FarmingAreaId" uuid NOT NULL REFERENCES be."FarmingAreas"("Id") ON DELETE RESTRICT,
          "CameraDeviceId" uuid NOT NULL REFERENCES be."Devices"("Id") ON DELETE RESTRICT,
          "Analyte" text NOT NULL, "KitName" text NOT NULL, "ReagentLot" text NOT NULL, "Unit" text NOT NULL,
          "ProtocolJson" text NOT NULL, "CurveJson" text NOT NULL, "ValidationJson" text NOT NULL,
          "ValidUntil" timestamptz NOT NULL, "RevokedAt" timestamptz NULL, "CreatedBy" uuid NOT NULL,
          CHECK (("Analyte" IN ('NO2','NO3') AND "Unit" = 'mg/L ' || "Analyte") OR ("Analyte"='NH3' AND "Unit" IN ('mg/L NH3','mg/L NH4','mg/L TAN-N')))
        );
        CREATE INDEX "IX_WaterColorCalibrations_FarmingAreaId_Analyte" ON be."WaterColorCalibrations"("FarmingAreaId","Analyte");
        CREATE INDEX "IX_WaterColorCalibrations_CameraDeviceId" ON be."WaterColorCalibrations"("CameraDeviceId");
        CREATE TABLE be."WaterAnalysisSchedules" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "FarmingAreaId" uuid NOT NULL REFERENCES be."FarmingAreas"("Id") ON DELETE RESTRICT,
          "TargetsJson" text NOT NULL, "SampleSource" text NOT NULL, "IntervalMinutes" integer NOT NULL,
          "Enabled" boolean NOT NULL, "NextRunAt" timestamptz NOT NULL, "LastAttemptAt" timestamptz NULL, "LastOutcome" text NULL);
        CREATE UNIQUE INDEX "IX_WaterAnalysisSchedules_FarmingAreaId" ON be."WaterAnalysisSchedules"("FarmingAreaId");
        CREATE TABLE be."WaterAnalysisBatches" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "FarmingAreaId" uuid NOT NULL REFERENCES be."FarmingAreas"("Id") ON DELETE RESTRICT,
          "Trigger" text NOT NULL, "Status" text NOT NULL, "TargetsJson" text NOT NULL, "SampleSource" text NOT NULL,
          "ScheduleId" uuid NULL, "ScheduledFor" timestamptz NULL, "RequestedBy" uuid NULL, "CompletedAt" timestamptz NULL);
        CREATE UNIQUE INDEX "IX_WaterAnalysisBatches_FarmingAreaId" ON be."WaterAnalysisBatches"("FarmingAreaId") WHERE "Status"='waiting_samples';
        ALTER TABLE be."WaterAnalysisRuns" ADD COLUMN "BatchId" uuid NULL REFERENCES be."WaterAnalysisBatches"("Id") ON DELETE RESTRICT,
          ADD COLUMN "MeasuredValue" numeric NULL, ADD COLUMN "MeasuredUnit" text NULL;
        CREATE UNIQUE INDEX "IX_WaterAnalysisRuns_BatchId_Analyte" ON be."WaterAnalysisRuns"("BatchId","Analyte");
        ALTER TABLE be."WaterAnalysisRuns" ADD COLUMN "CalibrationId" uuid NULL REFERENCES be."WaterColorCalibrations"("Id") ON DELETE RESTRICT,
          ADD COLUMN "SampleMediaId" uuid NULL REFERENCES be."MediaAssets"("Id") ON DELETE RESTRICT, ADD COLUMN "ColorEvidenceJson" text NULL;
        CREATE INDEX "IX_WaterAnalysisRuns_CalibrationId" ON be."WaterAnalysisRuns"("CalibrationId");
        CREATE UNIQUE INDEX "IX_WaterAnalysisRuns_SampleMediaId" ON be."WaterAnalysisRuns"("SampleMediaId");
        """);
    protected override void Down(MigrationBuilder m) => m.Sql("""
        ALTER TABLE be."WaterAnalysisRuns" DROP COLUMN "CalibrationId", DROP COLUMN "SampleMediaId", DROP COLUMN "ColorEvidenceJson", DROP COLUMN "BatchId", DROP COLUMN "MeasuredValue", DROP COLUMN "MeasuredUnit";
        DROP TABLE be."WaterAnalysisBatches", be."WaterAnalysisSchedules";
        DROP TABLE be."WaterColorCalibrations";
        """);
}
