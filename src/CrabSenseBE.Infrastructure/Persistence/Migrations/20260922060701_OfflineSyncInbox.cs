using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfflineSyncInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The development database already contains several model changes
            // that were never recorded in its migration history. Apply only
            // the sync inbox here; those legacy changes are baseline data.
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS be."SyncInboxItems" (
                    "Id" uuid NOT NULL,
                    "IdempotencyKey" character varying(128) NOT NULL,
                    "EntityType" character varying(64) NOT NULL,
                    "EntityId" character varying(128) NOT NULL,
                    "OperationType" character varying(128) NOT NULL,
                    "BaseVersion" integer NULL,
                    "ClientUpdatedAt" timestamp with time zone NULL,
                    "PayloadJson" jsonb NOT NULL,
                    "Status" character varying(32) NOT NULL,
                    "ErrorMessage" text NULL,
                    "ReceivedAt" timestamp with time zone NOT NULL,
                    "ProcessedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_SyncInboxItems" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_SyncInboxItems_IdempotencyKey"
                    ON be."SyncInboxItems" ("IdempotencyKey");
                CREATE INDEX IF NOT EXISTS "IX_SyncInboxItems_Status_ReceivedAt"
                    ON be."SyncInboxItems" ("Status", "ReceivedAt");
                """);
            return;

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                schema: "be",
                table: "SalesOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                schema: "be",
                table: "SalesOrders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "FarmingAreaId",
                schema: "be",
                table: "SalesOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                schema: "be",
                table: "SalesOrders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                schema: "be",
                table: "SalesOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                schema: "be",
                table: "SalesOrders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                schema: "be",
                table: "SalesOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingFee",
                schema: "be",
                table: "SalesOrders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SubtotalAmount",
                schema: "be",
                table: "SalesOrders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CrabCode",
                schema: "be",
                table: "SalesOrderLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CrabId",
                schema: "be",
                table: "SalesOrderLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrabType",
                schema: "be",
                table: "SalesOrderLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                schema: "be",
                table: "SalesOrderLines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightGram",
                schema: "be",
                table: "SalesOrderLines",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCommandAt",
                schema: "be",
                table: "RasComponents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RunStartedAt",
                schema: "be",
                table: "RasComponents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FarmingAreaId",
                schema: "be",
                table: "HarvestVouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PerformedByName",
                schema: "be",
                table: "HarvestVouchers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrlsJson",
                schema: "be",
                table: "HarvestVouchers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AreaName",
                schema: "be",
                table: "HarvestLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoxCode",
                schema: "be",
                table: "HarvestLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConditionLabel",
                schema: "be",
                table: "HarvestLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrabCode",
                schema: "be",
                table: "HarvestLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LotCode",
                schema: "be",
                table: "HarvestLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrlsJson",
                schema: "be",
                table: "HarvestLines",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Result",
                schema: "be",
                table: "HarvestLines",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RowName",
                schema: "be",
                table: "HarvestLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Appetite",
                schema: "be",
                table: "FarmOperations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Condition",
                schema: "be",
                table: "FarmOperations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrabIdsJson",
                schema: "be",
                table: "FarmOperations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FoodType",
                schema: "be",
                table: "FarmOperations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationLabel",
                schema: "be",
                table: "FarmOperations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "be",
                table: "FarmOperations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ScheduledFarmTasks",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    RecurrenceType = table.Column<string>(type: "text", nullable: false),
                    DaysOfWeekJson = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReminderMinuteOfDay = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledFarmTasks", x => x.Id);
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

            migrationBuilder.CreateTable(
                name: "WaterAnalysisRuns",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentStep = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastStepAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Ph = table.Column<decimal>(type: "numeric", nullable: true),
                    Nh3 = table.Column<decimal>(type: "numeric", nullable: true),
                    No2 = table.Column<decimal>(type: "numeric", nullable: true),
                    No3 = table.Column<decimal>(type: "numeric", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaterAnalysisRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaterAnalysisRuns_FarmingAreas_FarmingAreaId",
                        column: x => x.FarmingAreaId,
                        principalSchema: "be",
                        principalTable: "FarmingAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_FarmingAreaId",
                schema: "be",
                table: "SalesOrders",
                column: "FarmingAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestVouchers_FarmingAreaId",
                schema: "be",
                table: "HarvestVouchers",
                column: "FarmingAreaId");

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

            migrationBuilder.CreateIndex(
                name: "IX_WaterAnalysisRuns_FarmingAreaId_StartedAt",
                schema: "be",
                table: "WaterAnalysisRuns",
                columns: new[] { "FarmingAreaId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledFarmTasks",
                schema: "be");

            migrationBuilder.DropTable(
                name: "SyncInboxItems",
                schema: "be");

            migrationBuilder.DropTable(
                name: "WaterAnalysisRuns",
                schema: "be");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_FarmingAreaId",
                schema: "be",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_HarvestVouchers_FarmingAreaId",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                schema: "be",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                schema: "be",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "FarmingAreaId",
                schema: "be",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                schema: "be",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                schema: "be",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                schema: "be",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "SellerName",
                schema: "be",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "ShippingFee",
                schema: "be",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "SubtotalAmount",
                schema: "be",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CrabCode",
                schema: "be",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "CrabId",
                schema: "be",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "CrabType",
                schema: "be",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "Quantity",
                schema: "be",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "WeightGram",
                schema: "be",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "LastCommandAt",
                schema: "be",
                table: "RasComponents");

            migrationBuilder.DropColumn(
                name: "RunStartedAt",
                schema: "be",
                table: "RasComponents");

            migrationBuilder.DropColumn(
                name: "FarmingAreaId",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "PerformedByName",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "PhotoUrlsJson",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "AreaName",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropColumn(
                name: "BoxCode",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropColumn(
                name: "ConditionLabel",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropColumn(
                name: "CrabCode",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropColumn(
                name: "LotCode",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropColumn(
                name: "PhotoUrlsJson",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropColumn(
                name: "Result",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropColumn(
                name: "RowName",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropColumn(
                name: "Appetite",
                schema: "be",
                table: "FarmOperations");

            migrationBuilder.DropColumn(
                name: "Condition",
                schema: "be",
                table: "FarmOperations");

            migrationBuilder.DropColumn(
                name: "CrabIdsJson",
                schema: "be",
                table: "FarmOperations");

            migrationBuilder.DropColumn(
                name: "FoodType",
                schema: "be",
                table: "FarmOperations");

            migrationBuilder.DropColumn(
                name: "LocationLabel",
                schema: "be",
                table: "FarmOperations");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "be",
                table: "FarmOperations");
        }
    }
}
