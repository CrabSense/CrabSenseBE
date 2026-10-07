using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EdgeCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CameraId",
                schema: "be",
                table: "MoltingRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                schema: "be",
                table: "MoltingRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrlsJson",
                schema: "be",
                table: "MoltingRecords",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ShellLengthAfterMm",
                schema: "be",
                table: "MoltingRecords",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShellLengthBeforeMm",
                schema: "be",
                table: "MoltingRecords",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShellWidthAfterMm",
                schema: "be",
                table: "MoltingRecords",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShellWidthBeforeMm",
                schema: "be",
                table: "MoltingRecords",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                schema: "be",
                table: "MoltingRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightBeforeGram",
                schema: "be",
                table: "MoltingRecords",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActivityAfter",
                schema: "be",
                table: "FarmOperations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActivityBefore",
                schema: "be",
                table: "FarmOperations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CameraId",
                schema: "be",
                table: "FarmOperations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EatenQuantity",
                schema: "be",
                table: "FarmOperations",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FeedingDurationMinutes",
                schema: "be",
                table: "FarmOperations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                schema: "be",
                table: "FarmingAreas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                schema: "be",
                table: "FarmingAreas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CarapaceLengthMm",
                schema: "be",
                table: "CrabWeightHistories",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CarapaceWidthMm",
                schema: "be",
                table: "CrabWeightHistories",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrlsJson",
                schema: "be",
                table: "CrabWeightHistories",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecordedByName",
                schema: "be",
                table: "CrabWeightHistories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrlsJson",
                schema: "be",
                table: "CrabLots",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "EdgeCommands",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceCode = table.Column<string>(type: "text", nullable: false),
                    Command = table.Column<string>(type: "text", nullable: false),
                    Channel = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultMessage = table.Column<string>(type: "text", nullable: true),
                    CorrelationId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EdgeCommands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncInboxItems",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    OperationType = table.Column<string>(type: "text", nullable: false),
                    BaseVersion = table.Column<int>(type: "integer", nullable: true),
                    ClientUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncInboxItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EdgeCommands_DeviceCode_Status_CreatedAt",
                schema: "be",
                table: "EdgeCommands",
                columns: new[] { "DeviceCode", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncInboxItems_IdempotencyKey",
                schema: "be",
                table: "SyncInboxItems",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncInboxItems_Status_ReceivedAt",
                schema: "be",
                table: "SyncInboxItems",
                columns: new[] { "Status", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EdgeCommands",
                schema: "be");

            migrationBuilder.DropTable(
                name: "SyncInboxItems",
                schema: "be");

            migrationBuilder.DropColumn(
                name: "CameraId",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "PhotoUrlsJson",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "ShellLengthAfterMm",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "ShellLengthBeforeMm",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "ShellWidthAfterMm",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "ShellWidthBeforeMm",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "WeightBeforeGram",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "ActivityAfter",
                schema: "be",
                table: "FarmOperations");

            migrationBuilder.DropColumn(
                name: "ActivityBefore",
                schema: "be",
                table: "FarmOperations");

            migrationBuilder.DropColumn(
                name: "CameraId",
                schema: "be",
                table: "FarmOperations");

            migrationBuilder.DropColumn(
                name: "EatenQuantity",
                schema: "be",
                table: "FarmOperations");

            migrationBuilder.DropColumn(
                name: "FeedingDurationMinutes",
                schema: "be",
                table: "FarmOperations");

            migrationBuilder.DropColumn(
                name: "Latitude",
                schema: "be",
                table: "FarmingAreas");

            migrationBuilder.DropColumn(
                name: "Longitude",
                schema: "be",
                table: "FarmingAreas");

            migrationBuilder.DropColumn(
                name: "CarapaceLengthMm",
                schema: "be",
                table: "CrabWeightHistories");

            migrationBuilder.DropColumn(
                name: "CarapaceWidthMm",
                schema: "be",
                table: "CrabWeightHistories");

            migrationBuilder.DropColumn(
                name: "PhotoUrlsJson",
                schema: "be",
                table: "CrabWeightHistories");

            migrationBuilder.DropColumn(
                name: "RecordedByName",
                schema: "be",
                table: "CrabWeightHistories");

            migrationBuilder.DropColumn(
                name: "ImageUrlsJson",
                schema: "be",
                table: "CrabLots");
        }
    }
}
