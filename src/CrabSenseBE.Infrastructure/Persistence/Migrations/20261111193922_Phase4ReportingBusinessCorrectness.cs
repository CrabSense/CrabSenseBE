using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4ReportingBusinessCorrectness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WaterMeasurements_SensorId",
                schema: "be",
                table: "WaterMeasurements");

            migrationBuilder.DropIndex(
                name: "IX_FrozenInventoryAdjustments_FrozenLotId",
                schema: "be",
                table: "FrozenInventoryAdjustments");

            migrationBuilder.CreateIndex(
                name: "IX_WaterMeasurements_SensorId_MeasuredAt",
                schema: "be",
                table: "WaterMeasurements",
                columns: new[] { "SensorId", "MeasuredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingLabels_FeedingEventId",
                schema: "be",
                table: "TrainingLabels",
                column: "FeedingEventId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingLabels_ObservationEventId",
                schema: "be",
                table: "TrainingLabels",
                column: "ObservationEventId");

            // Existing legacy labels are retained for review. NOT VALID enforces the
            // rule for new/updated rows without making deployment fail on old data.
            migrationBuilder.Sql(
                """
                ALTER TABLE "be"."TrainingLabels"
                ADD CONSTRAINT "CK_TrainingLabels_ExactlyOneSource"
                CHECK (("FeedingEventId" IS NULL) <> ("ObservationEventId" IS NULL))
                NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ObservationEvents_BoxId",
                schema: "be",
                table: "ObservationEvents",
                column: "BoxId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservationEvents_CrabId_ObservedAt",
                schema: "be",
                table: "ObservationEvents",
                columns: new[] { "CrabId", "ObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ObservationEvents_FeedingEventId",
                schema: "be",
                table: "ObservationEvents",
                column: "FeedingEventId");

            migrationBuilder.CreateIndex(
                name: "IX_FrozenInventoryAdjustments_FrozenLotId_CreatedAt",
                schema: "be",
                table: "FrozenInventoryAdjustments",
                columns: new[] { "FrozenLotId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedingEvents_BoxId",
                schema: "be",
                table: "FeedingEvents",
                column: "BoxId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedingEvents_CrabId_FedAt",
                schema: "be",
                table: "FeedingEvents",
                columns: new[] { "CrabId", "FedAt" });

            // Preserve legacy rows for reconciliation while enforcing referential
            // integrity for all new writes.
            migrationBuilder.Sql(
                """
                ALTER TABLE "be"."FeedingEvents"
                ADD CONSTRAINT "FK_FeedingEvents_Boxes_BoxId"
                FOREIGN KEY ("BoxId") REFERENCES "be"."Boxes" ("Id")
                ON DELETE RESTRICT NOT VALID;
                ALTER TABLE "be"."FeedingEvents"
                ADD CONSTRAINT "FK_FeedingEvents_Crabs_CrabId"
                FOREIGN KEY ("CrabId") REFERENCES "be"."Crabs" ("Id")
                ON DELETE RESTRICT NOT VALID;
                ALTER TABLE "be"."ObservationEvents"
                ADD CONSTRAINT "FK_ObservationEvents_Boxes_BoxId"
                FOREIGN KEY ("BoxId") REFERENCES "be"."Boxes" ("Id")
                ON DELETE RESTRICT NOT VALID;
                ALTER TABLE "be"."ObservationEvents"
                ADD CONSTRAINT "FK_ObservationEvents_Crabs_CrabId"
                FOREIGN KEY ("CrabId") REFERENCES "be"."Crabs" ("Id")
                ON DELETE RESTRICT NOT VALID;
                ALTER TABLE "be"."ObservationEvents"
                ADD CONSTRAINT "FK_ObservationEvents_FeedingEvents_FeedingEventId"
                FOREIGN KEY ("FeedingEventId") REFERENCES "be"."FeedingEvents" ("Id")
                ON DELETE RESTRICT NOT VALID;
                ALTER TABLE "be"."TrainingLabels"
                ADD CONSTRAINT "FK_TrainingLabels_FeedingEvents_FeedingEventId"
                FOREIGN KEY ("FeedingEventId") REFERENCES "be"."FeedingEvents" ("Id")
                ON DELETE RESTRICT NOT VALID;
                ALTER TABLE "be"."TrainingLabels"
                ADD CONSTRAINT "FK_TrainingLabels_ObservationEvents_ObservationEventId"
                FOREIGN KEY ("ObservationEventId") REFERENCES "be"."ObservationEvents" ("Id")
                ON DELETE RESTRICT NOT VALID;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FeedingEvents_Boxes_BoxId",
                schema: "be",
                table: "FeedingEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_FeedingEvents_Crabs_CrabId",
                schema: "be",
                table: "FeedingEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_ObservationEvents_Boxes_BoxId",
                schema: "be",
                table: "ObservationEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_ObservationEvents_Crabs_CrabId",
                schema: "be",
                table: "ObservationEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_ObservationEvents_FeedingEvents_FeedingEventId",
                schema: "be",
                table: "ObservationEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingLabels_FeedingEvents_FeedingEventId",
                schema: "be",
                table: "TrainingLabels");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingLabels_ObservationEvents_ObservationEventId",
                schema: "be",
                table: "TrainingLabels");

            migrationBuilder.DropIndex(
                name: "IX_WaterMeasurements_SensorId_MeasuredAt",
                schema: "be",
                table: "WaterMeasurements");

            migrationBuilder.DropIndex(
                name: "IX_TrainingLabels_FeedingEventId",
                schema: "be",
                table: "TrainingLabels");

            migrationBuilder.DropIndex(
                name: "IX_TrainingLabels_ObservationEventId",
                schema: "be",
                table: "TrainingLabels");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrainingLabels_ExactlyOneSource",
                schema: "be",
                table: "TrainingLabels");

            migrationBuilder.DropIndex(
                name: "IX_ObservationEvents_BoxId",
                schema: "be",
                table: "ObservationEvents");

            migrationBuilder.DropIndex(
                name: "IX_ObservationEvents_CrabId_ObservedAt",
                schema: "be",
                table: "ObservationEvents");

            migrationBuilder.DropIndex(
                name: "IX_ObservationEvents_FeedingEventId",
                schema: "be",
                table: "ObservationEvents");

            migrationBuilder.DropIndex(
                name: "IX_FrozenInventoryAdjustments_FrozenLotId_CreatedAt",
                schema: "be",
                table: "FrozenInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_FeedingEvents_BoxId",
                schema: "be",
                table: "FeedingEvents");

            migrationBuilder.DropIndex(
                name: "IX_FeedingEvents_CrabId_FedAt",
                schema: "be",
                table: "FeedingEvents");

            migrationBuilder.CreateIndex(
                name: "IX_WaterMeasurements_SensorId",
                schema: "be",
                table: "WaterMeasurements",
                column: "SensorId");

            migrationBuilder.CreateIndex(
                name: "IX_FrozenInventoryAdjustments_FrozenLotId",
                schema: "be",
                table: "FrozenInventoryAdjustments",
                column: "FrozenLotId");
        }
    }
}
