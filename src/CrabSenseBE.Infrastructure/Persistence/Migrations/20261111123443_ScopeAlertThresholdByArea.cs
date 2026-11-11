using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeAlertThresholdByArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FarmingAreaId",
                schema: "be",
                table: "AlertThresholds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WaterSystemId",
                schema: "be",
                table: "AlertThresholds",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FarmingAreaId",
                schema: "be",
                table: "AlertThresholds");

            migrationBuilder.DropColumn(
                name: "WaterSystemId",
                schema: "be",
                table: "AlertThresholds");
        }
    }
}
