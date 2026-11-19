using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261116000000_WaterAiExternalCalibration")]
public sealed class WaterAiExternalCalibration : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE be."WaterColorCalibrations" ADD COLUMN "AiAlgorithm" text NULL,
          ADD COLUMN "AiCalibrationId" text NULL, ADD COLUMN "KitRevision" text NULL, ADD COLUMN "AiCalibrationToken" text NULL;
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE be."WaterColorCalibrations" DROP COLUMN "AiAlgorithm", DROP COLUMN "AiCalibrationId",
          DROP COLUMN "KitRevision", DROP COLUMN "AiCalibrationToken";
        """);
}
