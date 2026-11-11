using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFrozenInventoryOpeningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FrozenInventoryOpeningBalances",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FrozenLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpeningQuantity = table.Column<int>(type: "integer", nullable: false),
                    OpeningWeightKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    OpeningDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VerificationStatus = table.Column<string>(type: "text", nullable: false),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerificationNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrozenInventoryOpeningBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FrozenInventoryOpeningBalances_FarmingAreas_FarmingAreaId",
                        column: x => x.FarmingAreaId,
                        principalSchema: "be",
                        principalTable: "FarmingAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FrozenInventoryOpeningBalances_FrozenLots_FrozenLotId",
                        column: x => x.FrozenLotId,
                        principalSchema: "be",
                        principalTable: "FrozenLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FrozenInventoryOpeningBalances_FarmingAreaId_VerificationSt~",
                schema: "be",
                table: "FrozenInventoryOpeningBalances",
                columns: new[] { "FarmingAreaId", "VerificationStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_FrozenInventoryOpeningBalances_FrozenLotId",
                schema: "be",
                table: "FrozenInventoryOpeningBalances",
                column: "FrozenLotId",
                unique: true);

            // Run while writes are blocked so FrozenLots and the adjustment ledger are read
            // at one consistent cut-over point. The opening amount is reconstructed from the
            // current balance minus every movement recorded since the transaction ledger was
            // introduced. Existing inventory values are never updated here.
            migrationBuilder.Sql(
                """
                LOCK TABLE be."FrozenLots" IN SHARE ROW EXCLUSIVE MODE;
                LOCK TABLE be."FrozenInventoryAdjustments" IN SHARE ROW EXCLUSIVE MODE;

                WITH movements AS (
                    SELECT
                        adjustment."FrozenLotId",
                        COALESCE(SUM(adjustment."QuantityDelta"), 0)::integer AS quantity_delta,
                        COALESCE(SUM(adjustment."WeightDeltaKg"), 0)::numeric(12,3) AS weight_delta
                    FROM be."FrozenInventoryAdjustments" AS adjustment
                    GROUP BY adjustment."FrozenLotId"
                ),
                reconstructed AS (
                    SELECT
                        lot."Id" AS frozen_lot_id,
                        lot."FarmingAreaId" AS farming_area_id,
                        lot."Quantity" - COALESCE(movements.quantity_delta, 0) AS opening_quantity,
                        ROUND(
                            lot."WeightKg" - COALESCE(movements.weight_delta, 0),
                            3) AS opening_weight,
                        lot."Quantity" AS current_quantity,
                        lot."WeightKg" AS current_weight
                    FROM be."FrozenLots" AS lot
                    LEFT JOIN movements ON movements."FrozenLotId" = lot."Id"
                )
                INSERT INTO be."FrozenInventoryOpeningBalances" (
                    "Id",
                    "FrozenLotId",
                    "FarmingAreaId",
                    "OpeningQuantity",
                    "OpeningWeightKg",
                    "OpeningDate",
                    "Source",
                    "VerificationStatus",
                    "VerifiedByUserId",
                    "VerifiedAt",
                    "VerificationNote",
                    "CreatedAt",
                    "UpdatedAt")
                SELECT
                    md5(reconstructed.frozen_lot_id::text || ':frozen-opening-balance')::uuid,
                    reconstructed.frozen_lot_id,
                    reconstructed.farming_area_id,
                    reconstructed.opening_quantity,
                    reconstructed.opening_weight,
                    TIMESTAMPTZ '2026-11-11 13:28:19+00',
                    'LegacyFrozenLotReconciliation',
                    CASE
                        WHEN reconstructed.farming_area_id IS NULL
                          OR reconstructed.opening_quantity < 0
                          OR reconstructed.opening_weight < 0
                          OR ((reconstructed.opening_quantity = 0)
                              <> (reconstructed.opening_weight = 0))
                        THEN 'NeedsReview'
                        ELSE 'PendingReview'
                    END,
                    NULL,
                    NULL,
                    CASE
                        WHEN reconstructed.farming_area_id IS NULL
                        THEN 'Missing FarmingAreaId'
                        WHEN reconstructed.opening_quantity < 0
                          OR reconstructed.opening_weight < 0
                        THEN 'Reconstructed opening balance is negative'
                        WHEN ((reconstructed.opening_quantity = 0)
                              <> (reconstructed.opening_weight = 0))
                        THEN 'Opening quantity and weight have inconsistent zero values'
                        ELSE NULL
                    END,
                    transaction_timestamp(),
                    NULL
                FROM reconstructed
                ON CONFLICT ("FrozenLotId") DO NOTHING;
                """);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION be.prevent_verified_frozen_opening_balance_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF OLD."VerificationStatus" = 'Verified' THEN
                        RAISE EXCEPTION
                            'Verified frozen inventory opening balances are immutable';
                    END IF;
                    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
                END;
                $$;

                CREATE TRIGGER "TR_FrozenOpeningBalance_Immutable"
                BEFORE UPDATE OR DELETE
                ON be."FrozenInventoryOpeningBalances"
                FOR EACH ROW
                EXECUTE FUNCTION be.prevent_verified_frozen_opening_balance_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FrozenInventoryOpeningBalances",
                schema: "be");

            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS be.prevent_verified_frozen_opening_balance_change();");
        }
    }
}
