using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreventConcurrentWaterAnalysisRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM be."WaterAnalysisRuns"
                        WHERE "Status" IN ('running', 'stopping')
                        GROUP BY "FarmingAreaId"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION
                            'Duplicate active WaterAnalysisRuns require reviewed recovery before migration';
                    END IF;
                END $$;
                """);
            migrationBuilder.CreateIndex(
                name: "IX_WaterAnalysisRuns_FarmingAreaId",
                schema: "be",
                table: "WaterAnalysisRuns",
                column: "FarmingAreaId",
                unique: true,
                filter: "\"Status\" IN ('running', 'stopping')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WaterAnalysisRuns_FarmingAreaId",
                schema: "be",
                table: "WaterAnalysisRuns");
        }
    }
}
