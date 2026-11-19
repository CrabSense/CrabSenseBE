using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261116010000_WaterCalibrationQualityChecks")]
public sealed class WaterCalibrationQualityChecksMigration : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE be."WaterColorCalibrations" ADD COLUMN "QualityCheckDueAt" timestamptz NULL;
        UPDATE be."WaterColorCalibrations" SET "QualityCheckDueAt"=LEAST("ValidUntil","CreatedAt"+interval '7 days') WHERE "AiAlgorithm" IS NOT NULL;
        ALTER TABLE be."WaterAnalysisRuns" ADD COLUMN "SampleSha256" varchar(64) NULL;
        CREATE UNIQUE INDEX "IX_WaterAnalysisRuns_FarmingAreaId_SampleSha256" ON be."WaterAnalysisRuns"("FarmingAreaId","SampleSha256");
        CREATE TABLE be."WaterCalibrationChecks" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "CalibrationId" uuid NOT NULL REFERENCES be."WaterColorCalibrations"("Id") ON DELETE RESTRICT,
          "MediaId" uuid NOT NULL REFERENCES be."MediaAssets"("Id") ON DELETE RESTRICT,
          "PerformedBy" uuid NOT NULL, "Status" text NOT NULL, "Unit" text NOT NULL,
          "Expected" numeric NOT NULL, "Estimated" numeric NOT NULL, "AbsoluteError" numeric NOT NULL,
          "MaxError" numeric NOT NULL, "ImageSha256" varchar(64) NOT NULL, "EvidenceJson" text NOT NULL);
        CREATE UNIQUE INDEX "IX_WaterCalibrationChecks_CalibrationId_ImageSha256" ON be."WaterCalibrationChecks"("CalibrationId","ImageSha256");
        CREATE UNIQUE INDEX "IX_WaterCalibrationChecks_MediaId" ON be."WaterCalibrationChecks"("MediaId");
        CREATE INDEX "IX_WaterCalibrationChecks_CalibrationId_CreatedAt" ON be."WaterCalibrationChecks"("CalibrationId","CreatedAt");
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""
        DROP TABLE be."WaterCalibrationChecks";
        ALTER TABLE be."WaterAnalysisRuns" DROP COLUMN "SampleSha256";
        ALTER TABLE be."WaterColorCalibrations" DROP COLUMN "QualityCheckDueAt";
        """);
}
