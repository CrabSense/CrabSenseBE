using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261126110000_WaterBenchResultAssessments")]
public sealed class AddWaterBenchResultAssessments : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE be."WaterBenchResultAssessments" (
            "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
            "SessionEvidenceId" uuid NOT NULL REFERENCES be."WaterBenchEvidence"("Id") ON DELETE RESTRICT,
            "RequestId" uuid NOT NULL, "AssessedBy" uuid NOT NULL, "Status" varchar(32) NOT NULL,
            "RequestHash" varchar(64) NOT NULL, "Reason" varchar(500) NOT NULL,
            "ImageEvidenceId" uuid NULL REFERENCES be."WaterBenchEvidence"("Id") ON DELETE RESTRICT,
            "MediaId" uuid NULL REFERENCES be."MediaAssets"("Id") ON DELETE RESTRICT,
            "CalibrationId" uuid NULL REFERENCES be."WaterColorCalibrations"("Id") ON DELETE RESTRICT,
            "CandidateValue" numeric NULL, "Unit" varchar(32) NULL, "HoldoutError" numeric NULL, "EvidenceJson" text NOT NULL,
            CONSTRAINT "CK_WaterBenchAssessment_Status" CHECK (("Status" = 'needs_retest' AND "CandidateValue" IS NULL AND "Unit" IS NULL AND "HoldoutError" IS NULL AND "MediaId" IS NULL AND "CalibrationId" IS NULL AND "ImageEvidenceId" IS NULL) OR ("Status" = 'estimate_review_required' AND "CandidateValue" BETWEEN 0 AND 10000 AND "CandidateValue" IS NOT NULL AND "Unit" IS NOT NULL AND "HoldoutError" BETWEEN 0 AND 1000 AND "HoldoutError" IS NOT NULL AND "MediaId" IS NOT NULL AND "CalibrationId" IS NOT NULL AND "ImageEvidenceId" IS NOT NULL)),
            CONSTRAINT "CK_WaterBenchAssessment_Hash" CHECK ("RequestHash" ~ '^[0-9a-f]{64}$' AND octet_length("EvidenceJson") BETWEEN 2 AND 65536 AND jsonb_typeof("EvidenceJson"::jsonb) = 'object')
        );
        CREATE UNIQUE INDEX "IX_WaterBenchResultAssessments_SessionEvidenceId" ON be."WaterBenchResultAssessments"("SessionEvidenceId");
        CREATE UNIQUE INDEX "IX_WaterBenchResultAssessments_ImageEvidenceId" ON be."WaterBenchResultAssessments"("ImageEvidenceId");
        CREATE UNIQUE INDEX "IX_WaterBenchResultAssessments_MediaId" ON be."WaterBenchResultAssessments"("MediaId");
        CREATE INDEX "IX_WaterBenchResultAssessments_CalibrationId" ON be."WaterBenchResultAssessments"("CalibrationId");
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""DROP TABLE be."WaterBenchResultAssessments";""");
}
