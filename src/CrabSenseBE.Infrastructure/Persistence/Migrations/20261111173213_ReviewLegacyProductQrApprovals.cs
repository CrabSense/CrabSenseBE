using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewLegacyProductQrApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecisionReason",
                schema: "be",
                table: "QrCodes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // The historical approval migration used Approved as the default for every
            // existing QR. Product QR rows without both reviewer fields have no verifiable
            // approval evidence and must be reviewed by a Farm Manager.
            // Box identity QR, rejected decisions and IsActive are intentionally untouched.
            migrationBuilder.Sql(
                """
                UPDATE be."QrCodes"
                SET "ApprovalStatus" = 'Pending'
                WHERE "EntityType" IN ('frozen_lot', 'harvest_voucher')
                  AND "ApprovalStatus" = 'Approved'
                  AND ("ApprovedByUserId" IS NULL OR "ApprovedAt" IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecisionReason",
                schema: "be",
                table: "QrCodes");

            // Approval statuses are intentionally not restored: re-approving product QR
            // without evidence would expose unverified traceability data.
        }
    }
}
