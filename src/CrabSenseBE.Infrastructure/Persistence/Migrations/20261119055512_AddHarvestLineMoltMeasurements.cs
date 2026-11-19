using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHarvestLineMoltMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Some existing deployments already have these columns at numeric(8,2),
            // while a clean database built from the earlier migrations does not.
            // Ensure both columns exist before widening their precision.
            migrationBuilder.Sql("""
                ALTER TABLE be."HarvestLines"
                    ADD COLUMN IF NOT EXISTS "CarapaceLengthMm" numeric(8,2);
                ALTER TABLE be."HarvestLines"
                    ADD COLUMN IF NOT EXISTS "CarapaceWidthMm" numeric(8,2);
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "CarapaceLengthMm",
                schema: "be",
                table: "HarvestLines",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldPrecision: 8,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "CarapaceWidthMm",
                schema: "be",
                table: "HarvestLines",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldPrecision: 8,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HarvestCarapaceLengthMm",
                schema: "be",
                table: "HarvestAssignmentLines",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HarvestCarapaceWidthMm",
                schema: "be",
                table: "HarvestAssignmentLines",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingHarvestedAt",
                schema: "be",
                table: "HarvestAssignmentLines",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingHarvestedAt",
                schema: "be",
                table: "HarvestAssignmentLines");

            migrationBuilder.DropColumn(
                name: "HarvestCarapaceWidthMm",
                schema: "be",
                table: "HarvestAssignmentLines");

            migrationBuilder.DropColumn(
                name: "HarvestCarapaceLengthMm",
                schema: "be",
                table: "HarvestAssignmentLines");

            migrationBuilder.AlterColumn<decimal>(
                name: "CarapaceWidthMm",
                schema: "be",
                table: "HarvestLines",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "CarapaceLengthMm",
                schema: "be",
                table: "HarvestLines",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
