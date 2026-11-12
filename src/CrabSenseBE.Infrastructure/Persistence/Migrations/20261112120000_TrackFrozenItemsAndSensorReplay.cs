using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    public partial class TrackFrozenItemsAndSensorReplay : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>("StockState", "FrozenCrabItems", schema: "be", type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Unverified");
            migrationBuilder.AddColumn<DateTime>("DispatchedAt", "FrozenCrabItems", schema: "be", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<Guid>("OutboundTransactionId", "FrozenCrabItems", schema: "be", type: "uuid", nullable: true);
            migrationBuilder.CreateIndex("IX_FrozenCrabItems_OutboundTransactionId", "FrozenCrabItems", "OutboundTransactionId", schema: "be");
            migrationBuilder.AddForeignKey("FK_FrozenCrabItems_FrozenStorageTransactions_OutboundTransactionId",
                "FrozenCrabItems", "OutboundTransactionId", "FrozenStorageTransactions", principalColumn: "Id",
                schema: "be", principalSchema: "be", onDelete: ReferentialAction.Restrict);
            // Only certify historical items if there is no recorded outbound and
            // every barcode still matches the lot's current quantity and weight.
            migrationBuilder.Sql("""
                UPDATE be."FrozenCrabItems" item SET "StockState" = 'Available'
                FROM be."FrozenLots" lot
                WHERE item."FrozenLotId" = lot."Id"
                  AND lot."Quantity" > 0
                  AND lot."FarmingAreaId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM be."FrozenInventoryAdjustments" a
                    WHERE a."FrozenLotId" = lot."Id" AND a."QuantityDelta" < 0)
                  AND (SELECT count(*) FROM be."FrozenCrabItems" i WHERE i."FrozenLotId" = lot."Id") = lot."Quantity"
                  AND (SELECT round(sum(i."WeightGram") / 1000, 3) FROM be."FrozenCrabItems" i WHERE i."FrozenLotId" = lot."Id") = lot."WeightKg";
                """);
            migrationBuilder.AddColumn<string>("EdgeEventKey", "WaterMeasurements", schema: "be", type: "character varying(64)", maxLength: 64, nullable: true);
            // Preserve legacy duplicates; new writes get a bounded hash and a
            // unique per-sensor key. Existing Source is checked before insert.
            migrationBuilder.CreateIndex("IX_WaterMeasurements_SensorId_EdgeEventKey", "WaterMeasurements",
                new[] { "SensorId", "EdgeEventKey" }, schema: "be", unique: true, filter: "\"EdgeEventKey\" IS NOT NULL");
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM be."FrozenCrabItems" WHERE "StockState" <> 'Unverified')
                        OR EXISTS (SELECT 1 FROM be."WaterMeasurements" WHERE "EdgeEventKey" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot discard inventory or replay evidence; recover from verified backup.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex("IX_WaterMeasurements_SensorId_EdgeEventKey", "WaterMeasurements", schema: "be");
            migrationBuilder.DropColumn("EdgeEventKey", "WaterMeasurements", schema: "be");
            migrationBuilder.DropForeignKey("FK_FrozenCrabItems_FrozenStorageTransactions_OutboundTransactionId", "FrozenCrabItems", schema: "be");
            migrationBuilder.DropIndex("IX_FrozenCrabItems_OutboundTransactionId", "FrozenCrabItems", schema: "be");
            migrationBuilder.DropColumn("OutboundTransactionId", "FrozenCrabItems", schema: "be");
            migrationBuilder.DropColumn("DispatchedAt", "FrozenCrabItems", schema: "be");
            migrationBuilder.DropColumn("StockState", "FrozenCrabItems", schema: "be");
        }
    }
}
