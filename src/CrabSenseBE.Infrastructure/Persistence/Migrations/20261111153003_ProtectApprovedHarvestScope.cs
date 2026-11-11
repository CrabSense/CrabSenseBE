using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectApprovedHarvestScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdjustmentReason",
                schema: "be",
                table: "HarvestVouchers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdjustmentRequestedAt",
                schema: "be",
                table: "HarvestVouchers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdjustmentRequestedByUserId",
                schema: "be",
                table: "HarvestVouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovalVersion",
                schema: "be",
                table: "HarvestVouchers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedQuantity",
                schema: "be",
                table: "HarvestVouchers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyVersion",
                schema: "be",
                table: "HarvestVouchers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionShortfallCount",
                schema: "be",
                table: "HarvestVouchers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExecutionShortfallReason",
                schema: "be",
                table: "HarvestVouchers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HarvestApprovedLines",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HarvestVoucherId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalVersion = table.Column<int>(type: "integer", nullable: false),
                    SourceHarvestLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    CrabId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoxId = table.Column<Guid>(type: "uuid", nullable: true),
                    FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CrabCode = table.Column<string>(type: "text", nullable: true),
                    BoxCode = table.Column<string>(type: "text", nullable: true),
                    LotCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HarvestApprovedLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HarvestApprovedLines_Boxes_BoxId",
                        column: x => x.BoxId,
                        principalSchema: "be",
                        principalTable: "Boxes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HarvestApprovedLines_Crabs_CrabId",
                        column: x => x.CrabId,
                        principalSchema: "be",
                        principalTable: "Crabs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HarvestApprovedLines_HarvestVouchers_HarvestVoucherId",
                        column: x => x.HarvestVoucherId,
                        principalSchema: "be",
                        principalTable: "HarvestVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HarvestApprovedLines_BoxId",
                schema: "be",
                table: "HarvestApprovedLines",
                column: "BoxId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestApprovedLines_CrabId",
                schema: "be",
                table: "HarvestApprovedLines",
                column: "CrabId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestApprovedLines_HarvestVoucherId_ApprovalVersion",
                schema: "be",
                table: "HarvestApprovedLines",
                columns: new[] { "HarvestVoucherId", "ApprovalVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_HarvestApprovedLines_HarvestVoucherId_ApprovalVersion_CrabId",
                schema: "be",
                table: "HarvestApprovedLines",
                columns: new[] { "HarvestVoucherId", "ApprovalVersion", "CrabId" },
                unique: true);

            // Preserve the currently approved scope for legacy vouchers. The
            // snapshot is copied; HarvestLines remain untouched as actual data.
            migrationBuilder.Sql(
                """
                INSERT INTO "be"."HarvestApprovedLines"
                    ("Id", "HarvestVoucherId", "ApprovalVersion", "SourceHarvestLineId",
                     "CrabId", "BoxId", "FarmingAreaId", "ApprovedByUserId",
                     "ApprovedAt", "CrabCode", "BoxCode", "LotCode",
                     "CreatedAt", "UpdatedAt")
                SELECT
                    md5('harvest-approved:' || line."Id"::text)::uuid,
                    voucher."Id",
                    1,
                    line."Id",
                    line."CrabId",
                    line."BoxId",
                    voucher."FarmingAreaId",
                    COALESCE(voucher."ApprovedByUserId", voucher."CreatedBy"),
                    COALESCE(voucher."ApprovedAt", voucher."UpdatedAt", voucher."CreatedAt"),
                    line."CrabCode",
                    line."BoxCode",
                    line."LotCode",
                    CURRENT_TIMESTAMP,
                    NULL
                FROM "be"."HarvestVouchers" voucher
                INNER JOIN "be"."HarvestLines" line
                    ON line."HarvestVoucherId" = voucher."Id"
                WHERE voucher."Status" IN ('Approved', 'InProgress', 'Completed')
                  AND voucher."FarmingAreaId" IS NOT NULL
                ON CONFLICT
                    ("HarvestVoucherId", "ApprovalVersion", "CrabId")
                    DO NOTHING;

                UPDATE "be"."HarvestVouchers" voucher
                SET "ApprovalVersion" = 1,
                    "ApprovedQuantity" =
                    (
                        SELECT COUNT(*)::integer
                        FROM "be"."HarvestApprovedLines" approved
                        WHERE approved."HarvestVoucherId" = voucher."Id"
                          AND approved."ApprovalVersion" = 1
                    )
                WHERE voucher."Status" IN ('Approved', 'InProgress', 'Completed')
                  AND EXISTS
                    (
                        SELECT 1
                        FROM "be"."HarvestApprovedLines" approved
                        WHERE approved."HarvestVoucherId" = voucher."Id"
                          AND approved."ApprovalVersion" = 1
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HarvestApprovedLines",
                schema: "be");

            migrationBuilder.DropColumn(
                name: "AdjustmentReason",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "AdjustmentRequestedAt",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "AdjustmentRequestedByUserId",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "ApprovalVersion",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "ApprovedQuantity",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "ExecutionShortfallCount",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "ExecutionShortfallReason",
                schema: "be",
                table: "HarvestVouchers");
        }
    }
}
