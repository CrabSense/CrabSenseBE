using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillHistoricalCrabLotAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrabLotAreaReviewItems",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CrabLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrabLotAreaReviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrabLotAreaReviewItems_CrabLots_CrabLotId",
                        column: x => x.CrabLotId,
                        principalSchema: "be",
                        principalTable: "CrabLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrabLotAreaReviewItems_CrabLotId",
                schema: "be",
                table: "CrabLotAreaReviewItems",
                column: "CrabLotId",
                unique: true);

            // Chỉ backfill từ quan hệ Crab -> Box đã được lưu trong Database.
            // UNION loại trùng giữa hộp hiện tại và các bảng lịch sử; unique index
            // CrabLotId + FarmingAreaId là lớp bảo vệ cuối cùng khi chạy migration.
            migrationBuilder.Sql(
                """
                WITH "TrustedLotAreas" AS
                (
                    SELECT c."CrabLotId", r."FarmingAreaId"
                    FROM "be"."CrabBoxAllocations" a
                    INNER JOIN "be"."Crabs" c ON c."Id" = a."CrabId"
                    INNER JOIN "be"."Boxes" b ON b."Id" = a."BoxId"
                    INNER JOIN "be"."FarmingRows" r ON r."Id" = b."FarmingRowId"

                    UNION

                    SELECT c."CrabLotId", r."FarmingAreaId"
                    FROM "be"."Crabs" c
                    INNER JOIN "be"."Boxes" b ON b."Id" = c."BoxId"
                    INNER JOIN "be"."FarmingRows" r ON r."Id" = b."FarmingRowId"

                    UNION

                    SELECT c."CrabLotId", r."FarmingAreaId"
                    FROM "be"."MoltingRecords" m
                    INNER JOIN "be"."Crabs" c ON c."Id" = m."CrabId"
                    INNER JOIN "be"."Boxes" b ON b."Id" = m."BoxId"
                    INNER JOIN "be"."FarmingRows" r ON r."Id" = b."FarmingRowId"

                    UNION

                    SELECT c."CrabLotId", r."FarmingAreaId"
                    FROM "be"."CrabMortalityRecords" m
                    INNER JOIN "be"."Crabs" c ON c."Id" = m."CrabId"
                    INNER JOIN "be"."Boxes" b ON b."Id" = m."BoxId"
                    INNER JOIN "be"."FarmingRows" r ON r."Id" = b."FarmingRowId"

                    UNION

                    SELECT c."CrabLotId", r."FarmingAreaId"
                    FROM "be"."HarvestLines" h
                    INNER JOIN "be"."Crabs" c ON c."Id" = h."CrabId"
                    INNER JOIN "be"."Boxes" b ON b."Id" = h."BoxId"
                    INNER JOIN "be"."FarmingRows" r ON r."Id" = b."FarmingRowId"
                )
                INSERT INTO "be"."CrabLotAreas"
                    ("Id", "CrabLotId", "FarmingAreaId", "CreatedAt", "UpdatedAt")
                SELECT
                    md5(t."CrabLotId"::text || ':' || t."FarmingAreaId"::text)::uuid,
                    t."CrabLotId",
                    t."FarmingAreaId",
                    CURRENT_TIMESTAMP,
                    NULL
                FROM "TrustedLotAreas" t
                ON CONFLICT ("CrabLotId", "FarmingAreaId") DO NOTHING;
                """);

            // Không suy đoán khu cho Lot không có bất kỳ nguồn hộp đáng tin cậy nào.
            // Các Lot này được lưu riêng để quản trị viên rà soát thủ công.
            migrationBuilder.Sql(
                """
                INSERT INTO "be"."CrabLotAreaReviewItems"
                    ("Id", "CrabLotId", "Reason", "ResolvedAt",
                     "ResolutionNotes", "CreatedAt", "UpdatedAt")
                SELECT
                    md5('crab-lot-area-review:' || l."Id"::text)::uuid,
                    l."Id",
                    'Không tìm thấy khu từ hộp hiện tại hoặc lịch sử nghiệp vụ của cua.',
                    NULL,
                    NULL,
                    CURRENT_TIMESTAMP,
                    NULL
                FROM "be"."CrabLots" l
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM "be"."CrabLotAreas" link
                    WHERE link."CrabLotId" = l."Id"
                )
                ON CONFLICT ("CrabLotId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrabLotAreaReviewItems",
                schema: "be");
        }
    }
}
