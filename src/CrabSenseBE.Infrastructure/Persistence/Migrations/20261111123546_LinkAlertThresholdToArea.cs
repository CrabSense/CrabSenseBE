using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkAlertThresholdToArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AlertThresholds_FarmingAreaId",
                schema: "be",
                table: "AlertThresholds",
                column: "FarmingAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertThresholds_WaterSystemId",
                schema: "be",
                table: "AlertThresholds",
                column: "WaterSystemId");

            migrationBuilder.AddForeignKey(
                name: "FK_AlertThresholds_FarmingAreas_FarmingAreaId",
                schema: "be",
                table: "AlertThresholds",
                column: "FarmingAreaId",
                principalSchema: "be",
                principalTable: "FarmingAreas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AlertThresholds_WaterSystems_WaterSystemId",
                schema: "be",
                table: "AlertThresholds",
                column: "WaterSystemId",
                principalSchema: "be",
                principalTable: "WaterSystems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AlertThresholds_FarmingAreas_FarmingAreaId",
                schema: "be",
                table: "AlertThresholds");

            migrationBuilder.DropForeignKey(
                name: "FK_AlertThresholds_WaterSystems_WaterSystemId",
                schema: "be",
                table: "AlertThresholds");

            migrationBuilder.DropIndex(
                name: "IX_AlertThresholds_FarmingAreaId",
                schema: "be",
                table: "AlertThresholds");

            migrationBuilder.DropIndex(
                name: "IX_AlertThresholds_WaterSystemId",
                schema: "be",
                table: "AlertThresholds");
        }
    }
}
