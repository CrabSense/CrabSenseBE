using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitHarvestApprovalAndExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                schema: "be",
                table: "HarvestVouchers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                schema: "be",
                table: "HarvestVouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                schema: "be",
                table: "HarvestVouchers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByUserId",
                schema: "be",
                table: "HarvestVouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutionCompletedAt",
                schema: "be",
                table: "HarvestVouchers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PerformedByUserId",
                schema: "be",
                table: "HarvestVouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "be",
                table: "HarvestVouchers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "ExecutionCompletedAt",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "PerformedByUserId",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "be",
                table: "HarvestVouchers");
        }
    }
}
