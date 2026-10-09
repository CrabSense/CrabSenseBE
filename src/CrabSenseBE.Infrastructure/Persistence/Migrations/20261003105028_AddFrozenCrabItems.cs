using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFrozenCrabItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FoodResponse",
                schema: "be",
                table: "TrainingLabels");

            migrationBuilder.AddColumn<string>(
                name: "Analyte",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CameraId",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Confidence",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ControllerId",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HardwareJson",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PerformedBy",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SampleLocation",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SampleSource",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StepLogJson",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestCode",
                schema: "be",
                table: "WaterAnalysisRuns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncidentId",
                schema: "be",
                table: "Alerts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastOccurredAt",
                schema: "be",
                table: "Alerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OccurrenceCount",
                schema: "be",
                table: "Alerts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingBy",
                schema: "be",
                table: "Alerts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAt",
                schema: "be",
                table: "Alerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionAction",
                schema: "be",
                table: "Alerts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                schema: "be",
                table: "Alerts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionReason",
                schema: "be",
                table: "Alerts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                schema: "be",
                table: "Alerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedBy",
                schema: "be",
                table: "Alerts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FrozenCrabItems",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FrozenLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    HarvestLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    CrabId = table.Column<Guid>(type: "uuid", nullable: false),
                    BarcodeValue = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LotCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CrabCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HarvestVoucherCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HarvestDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FrozenDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WeightGram = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Grade = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrozenCrabItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FrozenCrabItems_Crabs_CrabId",
                        column: x => x.CrabId,
                        principalSchema: "be",
                        principalTable: "Crabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FrozenCrabItems_FrozenLots_FrozenLotId",
                        column: x => x.FrozenLotId,
                        principalSchema: "be",
                        principalTable: "FrozenLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FrozenCrabItems_HarvestLines_HarvestLineId",
                        column: x => x.HarvestLineId,
                        principalSchema: "be",
                        principalTable: "HarvestLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FrozenCrabItems_BarcodeValue",
                schema: "be",
                table: "FrozenCrabItems",
                column: "BarcodeValue",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FrozenCrabItems_CrabId",
                schema: "be",
                table: "FrozenCrabItems",
                column: "CrabId");

            migrationBuilder.CreateIndex(
                name: "IX_FrozenCrabItems_FrozenLotId",
                schema: "be",
                table: "FrozenCrabItems",
                column: "FrozenLotId");

            migrationBuilder.CreateIndex(
                name: "IX_FrozenCrabItems_HarvestLineId",
                schema: "be",
                table: "FrozenCrabItems",
                column: "HarvestLineId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FrozenCrabItems",
                schema: "be");

            migrationBuilder.DropColumn(
                name: "Analyte",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "CameraId",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "Confidence",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "ControllerId",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "HardwareJson",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "PerformedBy",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "SampleLocation",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "SampleSource",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "StepLogJson",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "TestCode",
                schema: "be",
                table: "WaterAnalysisRuns");

            migrationBuilder.DropColumn(
                name: "IncidentId",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "LastOccurredAt",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "OccurrenceCount",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "ProcessingBy",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAt",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "ResolutionAction",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "ResolutionReason",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "ResolvedBy",
                schema: "be",
                table: "Alerts");

            migrationBuilder.AddColumn<string>(
                name: "FoodResponse",
                schema: "be",
                table: "TrainingLabels",
                type: "text",
                nullable: true);
        }
    }
}
