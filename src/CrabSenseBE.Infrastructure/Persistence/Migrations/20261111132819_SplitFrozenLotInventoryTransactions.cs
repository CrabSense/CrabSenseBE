using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitFrozenLotInventoryTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FarmingAreaId",
                schema: "be",
                table: "FrozenLots",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE be."FrozenLots" AS lot
                SET "FarmingAreaId" = voucher."FarmingAreaId"
                FROM be."HarvestVouchers" AS voucher
                WHERE lot."HarvestVoucherId" = voucher."Id"
                  AND voucher."FarmingAreaId" IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "FrozenStorageTransactions",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FrozenLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestedQuantity = table.Column<int>(type: "integer", nullable: false),
                    RequestedWeightKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    ActualQuantity = table.Column<int>(type: "integer", nullable: true),
                    ActualWeightKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PerformedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrozenStorageTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FrozenStorageTransactions_FarmingAreas_FarmingAreaId",
                        column: x => x.FarmingAreaId,
                        principalSchema: "be",
                        principalTable: "FarmingAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FrozenStorageTransactions_FrozenLots_FrozenLotId",
                        column: x => x.FrozenLotId,
                        principalSchema: "be",
                        principalTable: "FrozenLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FrozenInventoryAdjustments",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FrozenLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    FrozenStorageTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityDelta = table.Column<int>(type: "integer", nullable: false),
                    WeightDeltaKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    QuantityBefore = table.Column<int>(type: "integer", nullable: false),
                    QuantityAfter = table.Column<int>(type: "integer", nullable: false),
                    WeightBeforeKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    WeightAfterKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrozenInventoryAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FrozenInventoryAdjustments_FrozenLots_FrozenLotId",
                        column: x => x.FrozenLotId,
                        principalSchema: "be",
                        principalTable: "FrozenLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FrozenInventoryAdjustments_FrozenStorageTransactions_Frozen~",
                        column: x => x.FrozenStorageTransactionId,
                        principalSchema: "be",
                        principalTable: "FrozenStorageTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FrozenLots_FarmingAreaId",
                schema: "be",
                table: "FrozenLots",
                column: "FarmingAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_FrozenInventoryAdjustments_FarmingAreaId_CreatedAt",
                schema: "be",
                table: "FrozenInventoryAdjustments",
                columns: new[] { "FarmingAreaId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FrozenInventoryAdjustments_FrozenLotId",
                schema: "be",
                table: "FrozenInventoryAdjustments",
                column: "FrozenLotId");

            migrationBuilder.CreateIndex(
                name: "IX_FrozenInventoryAdjustments_FrozenStorageTransactionId",
                schema: "be",
                table: "FrozenInventoryAdjustments",
                column: "FrozenStorageTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FrozenStorageTransactions_FarmingAreaId_Status",
                schema: "be",
                table: "FrozenStorageTransactions",
                columns: new[] { "FarmingAreaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FrozenStorageTransactions_FrozenLotId",
                schema: "be",
                table: "FrozenStorageTransactions",
                column: "FrozenLotId");

            migrationBuilder.CreateIndex(
                name: "IX_FrozenStorageTransactions_TransactionCode",
                schema: "be",
                table: "FrozenStorageTransactions",
                column: "TransactionCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FrozenLots_FarmingAreas_FarmingAreaId",
                schema: "be",
                table: "FrozenLots",
                column: "FarmingAreaId",
                principalSchema: "be",
                principalTable: "FarmingAreas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FrozenLots_FarmingAreas_FarmingAreaId",
                schema: "be",
                table: "FrozenLots");

            migrationBuilder.DropTable(
                name: "FrozenInventoryAdjustments",
                schema: "be");

            migrationBuilder.DropTable(
                name: "FrozenStorageTransactions",
                schema: "be");

            migrationBuilder.DropIndex(
                name: "IX_FrozenLots_FarmingAreaId",
                schema: "be",
                table: "FrozenLots");

            migrationBuilder.DropColumn(
                name: "FarmingAreaId",
                schema: "be",
                table: "FrozenLots");
        }
    }
}
