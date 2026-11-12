using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2EdgeHardwareSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AwaitingEvent",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CleaningConfirmedAt",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutionMode",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "text",
                nullable: false,
                defaultValue: "simulation");

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultRecordedAt",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StepDeadlineAt",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                schema: "be",
                table: "EdgeCommands",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyVersion",
                schema: "be",
                table: "EdgeCommands",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "FailedAt",
                schema: "be",
                table: "EdgeCommands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                schema: "be",
                table: "EdgeCommands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                schema: "be",
                table: "EdgeCommands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseHolder",
                schema: "be",
                table: "EdgeCommands",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                schema: "be",
                table: "EdgeCommands",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EdgeCommands_DeviceCode_CorrelationId",
                schema: "be",
                table: "EdgeCommands",
                columns: new[] { "DeviceCode", "CorrelationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EdgeCommands_DeviceCode_CorrelationId",
                schema: "be",
                table: "EdgeCommands");

            migrationBuilder.DropColumn(
                name: "AwaitingEvent",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "CleaningConfirmedAt",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "ExecutionMode",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "ResultRecordedAt",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "StepDeadlineAt",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                schema: "be",
                table: "EdgeCommands");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                schema: "be",
                table: "EdgeCommands");

            migrationBuilder.DropColumn(
                name: "FailedAt",
                schema: "be",
                table: "EdgeCommands");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                schema: "be",
                table: "EdgeCommands");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                schema: "be",
                table: "EdgeCommands");

            migrationBuilder.DropColumn(
                name: "LeaseHolder",
                schema: "be",
                table: "EdgeCommands");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                schema: "be",
                table: "EdgeCommands");
        }
    }
}
