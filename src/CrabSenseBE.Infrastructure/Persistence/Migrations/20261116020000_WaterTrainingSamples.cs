using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261116020000_WaterTrainingSamples")]
public sealed class WaterTrainingSamplesMigration : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE be."WaterTrainingSamples" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "CalibrationId" uuid NOT NULL REFERENCES be."WaterColorCalibrations"("Id") ON DELETE RESTRICT,
          "FarmingAreaId" uuid NOT NULL REFERENCES be."FarmingAreas"("Id") ON DELETE RESTRICT,
          "MediaId" uuid NOT NULL REFERENCES be."MediaAssets"("Id") ON DELETE RESTRICT,
          "ImageSha256" varchar(64) NOT NULL, "PreparationId" text NOT NULL, "Split" text NOT NULL,
          "LabelSource" text NOT NULL, "ReferenceId" text NOT NULL, "Concentration" numeric NOT NULL,
          "ReactionSeconds" integer NOT NULL, "SalinityPpt" numeric NOT NULL, "ReviewedBy" uuid NOT NULL,
          "ReviewedAt" timestamptz NOT NULL, "WithdrawnAt" timestamptz NULL,
          CHECK ("Split" IN ('train','validation','test')),
          CHECK ("LabelSource" IN ('certified_standard','reference_lab')),
          CHECK ("Concentration">=0 AND "Concentration"<=10000));
        CREATE UNIQUE INDEX "IX_WaterTrainingSamples_FarmingAreaId_ImageSha256" ON be."WaterTrainingSamples"("FarmingAreaId","ImageSha256");
        CREATE UNIQUE INDEX "IX_WaterTrainingSamples_MediaId" ON be."WaterTrainingSamples"("MediaId");
        CREATE INDEX "IX_WaterTrainingSamples_CalibrationId_PreparationId" ON be."WaterTrainingSamples"("CalibrationId","PreparationId");
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""DROP TABLE be."WaterTrainingSamples";""");
}
