using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApproveProductQrAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                schema: "be",
                table: "QrCodes",
                type: "text",
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                schema: "be",
                table: "QrCodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                schema: "be",
                table: "QrCodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProposedByUserId",
                schema: "be",
                table: "QrCodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "be",
                table: "QrCodes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QrCodes_ApprovalStatus_IsActive",
                schema: "be",
                table: "QrCodes",
                columns: new[] { "ApprovalStatus", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QrCodes_ApprovalStatus_IsActive",
                schema: "be",
                table: "QrCodes");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                schema: "be",
                table: "QrCodes");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "be",
                table: "QrCodes");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                schema: "be",
                table: "QrCodes");

            migrationBuilder.DropColumn(
                name: "ProposedByUserId",
                schema: "be",
                table: "QrCodes");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "be",
                table: "QrCodes");
        }
    }
}
