using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HierarchyOwnerAndCropBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Crabs_CrabLots_CrabLotId",
                schema: "be",
                table: "Crabs");

            // ── Area.OwnerId (nullable → backfill → required) ───────────────
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                schema: "be",
                table: "FarmingAreas",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO be."AppUsers" (
                    "Id", "Username", "Email", "PasswordHash", "FullName", "Role", "IsActive", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), 'owner', 'owner@crabsense.local',
                       '$2a$11$placeholderhashforbootstraponlyxxxxxx.', 'Chủ trại',
                       'FarmOwner', TRUE, NOW() AT TIME ZONE 'utc', NOW() AT TIME ZONE 'utc'
                WHERE NOT EXISTS (SELECT 1 FROM be."AppUsers" LIMIT 1);

                UPDATE be."FarmingAreas" a
                SET "OwnerId" = COALESCE(
                    (SELECT u."Id" FROM be."AppUsers" u WHERE u."Username" = 'owner' LIMIT 1),
                    (SELECT u."Id" FROM be."AppUsers" u WHERE u."Role" = 'FarmOwner' ORDER BY u."CreatedAt" LIMIT 1),
                    (SELECT u."Id" FROM be."AppUsers" u ORDER BY u."CreatedAt" LIMIT 1)
                )
                WHERE a."OwnerId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerId",
                schema: "be",
                table: "FarmingAreas",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // ── Crab.CropBatchId + required CrabLotId ───────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "CropBatchId",
                schema: "be",
                table: "Crabs",
                type: "uuid",
                nullable: true);

            // Placeholder lot/batch for any legacy crabs missing parents
            migrationBuilder.Sql("""
                INSERT INTO be."CrabLots" ("Id", "LotCode", "ImportDate", "Quantity", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), 'LEGACY-LOT', NOW() AT TIME ZONE 'utc', 0, NOW() AT TIME ZONE 'utc', NOW() AT TIME ZONE 'utc'
                WHERE NOT EXISTS (SELECT 1 FROM be."CrabLots" WHERE "LotCode" = 'LEGACY-LOT')
                  AND EXISTS (SELECT 1 FROM be."Crabs" WHERE "CrabLotId" IS NULL);

                INSERT INTO be."CropBatches" ("Id", "BatchCode", "StartDate", "Status", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), 'LEGACY-BATCH', NOW() AT TIME ZONE 'utc', 'legacy', NOW() AT TIME ZONE 'utc', NOW() AT TIME ZONE 'utc'
                WHERE NOT EXISTS (SELECT 1 FROM be."CropBatches" WHERE "BatchCode" = 'LEGACY-BATCH')
                  AND (
                    EXISTS (SELECT 1 FROM be."Crabs" WHERE "CropBatchId" IS NULL)
                    OR EXISTS (SELECT 1 FROM be."Crabs")
                  );

                UPDATE be."Crabs" c
                SET "CrabLotId" = (SELECT l."Id" FROM be."CrabLots" l WHERE l."LotCode" = 'LEGACY-LOT' LIMIT 1)
                WHERE c."CrabLotId" IS NULL;

                UPDATE be."Crabs" c
                SET "CropBatchId" = COALESCE(
                    c."CropBatchId",
                    (SELECT b."Id" FROM be."CropBatches" b WHERE b."BatchCode" = 'LEGACY-BATCH' LIMIT 1),
                    (SELECT b."Id" FROM be."CropBatches" b ORDER BY b."CreatedAt" LIMIT 1)
                )
                WHERE c."CropBatchId" IS NULL;
                """);

            // If no crabs exist, still need a default for NOT NULL column add — ensure one batch exists when crabs present only.
            // For empty crabs table AlterColumn with default EmptyGuid would break FK — use conditional.

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM be."Crabs" WHERE "CropBatchId" IS NULL) THEN
                    RAISE EXCEPTION 'CropBatchId backfill failed';
                  END IF;
                  IF EXISTS (SELECT 1 FROM be."Crabs" WHERE "CrabLotId" IS NULL) THEN
                    RAISE EXCEPTION 'CrabLotId backfill failed';
                  END IF;
                  IF EXISTS (SELECT 1 FROM be."FarmingAreas" WHERE "OwnerId" IS NULL) THEN
                    RAISE EXCEPTION 'OwnerId backfill failed — seed at least one AppUser first';
                  END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CrabLotId",
                schema: "be",
                table: "Crabs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CropBatchId",
                schema: "be",
                table: "Crabs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FarmingAreas_OwnerId",
                schema: "be",
                table: "FarmingAreas",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Crabs_CropBatchId",
                schema: "be",
                table: "Crabs",
                column: "CropBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Crabs_CrabLots_CrabLotId",
                schema: "be",
                table: "Crabs",
                column: "CrabLotId",
                principalSchema: "be",
                principalTable: "CrabLots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Crabs_CropBatches_CropBatchId",
                schema: "be",
                table: "Crabs",
                column: "CropBatchId",
                principalSchema: "be",
                principalTable: "CropBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FarmingAreas_AppUsers_OwnerId",
                schema: "be",
                table: "FarmingAreas",
                column: "OwnerId",
                principalSchema: "be",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Crabs_CrabLots_CrabLotId",
                schema: "be",
                table: "Crabs");

            migrationBuilder.DropForeignKey(
                name: "FK_Crabs_CropBatches_CropBatchId",
                schema: "be",
                table: "Crabs");

            migrationBuilder.DropForeignKey(
                name: "FK_FarmingAreas_AppUsers_OwnerId",
                schema: "be",
                table: "FarmingAreas");

            migrationBuilder.DropIndex(
                name: "IX_FarmingAreas_OwnerId",
                schema: "be",
                table: "FarmingAreas");

            migrationBuilder.DropIndex(
                name: "IX_Crabs_CropBatchId",
                schema: "be",
                table: "Crabs");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "be",
                table: "FarmingAreas");

            migrationBuilder.DropColumn(
                name: "CropBatchId",
                schema: "be",
                table: "Crabs");

            migrationBuilder.AlterColumn<Guid>(
                name: "CrabLotId",
                schema: "be",
                table: "Crabs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_Crabs_CrabLots_CrabLotId",
                schema: "be",
                table: "Crabs",
                column: "CrabLotId",
                principalSchema: "be",
                principalTable: "CrabLots",
                principalColumn: "Id");
        }
    }
}
