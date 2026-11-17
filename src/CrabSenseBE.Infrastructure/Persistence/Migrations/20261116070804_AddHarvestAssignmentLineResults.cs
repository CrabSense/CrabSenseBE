using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHarvestAssignmentLineResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
    "DROP INDEX IF EXISTS be.\"IX_FarmStaffAssignments_ManagerUserId\";");

            migrationBuilder.AddColumn<Guid>(
                name: "HarvestAssignmentId",
                schema: "be",
                table: "HarvestVouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
    name: "ReviewStatus",
    schema: "be",
    table: "HarvestVouchers",
    type: "text",
    nullable: true);

            migrationBuilder.Sql(
                "UPDATE be.\"HarvestVouchers\" " +
                "SET \"ReviewStatus\" = 'NotSubmitted' " +
                "WHERE \"ReviewStatus\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "ReviewStatus",
                schema: "be",
                table: "HarvestVouchers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HarvestAssignmentLineId",
                schema: "be",
                table: "HarvestLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HarvestAssignments",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HarvestAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HarvestAssignments_AppUsers_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalSchema: "be",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HarvestAssignments_AppUsers_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalSchema: "be",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HarvestAssignments_FarmingAreas_FarmingAreaId",
                        column: x => x.FarmingAreaId,
                        principalSchema: "be",
                        principalTable: "FarmingAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HarvestAssignmentLines",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HarvestAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CrabId = table.Column<Guid>(type: "uuid", nullable: false),
                    CrabCode = table.Column<string>(type: "text", nullable: true),
                    BoxId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoxCode = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EligibilityReason = table.Column<string>(type: "text", nullable: true),
                    HarvestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Outcome = table.Column<string>(type: "text", nullable: true),
                    HarvestWeightGram = table.Column<decimal>(type: "numeric", nullable: true),
                    HarvestGrade = table.Column<string>(type: "text", nullable: true),
                    HarvestIsSoftshell = table.Column<bool>(type: "boolean", nullable: true),
                    HarvestConditionLabel = table.Column<string>(type: "text", nullable: true),
                    HarvestNotes = table.Column<string>(type: "text", nullable: true),
                    HarvestPhotoUrlsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HarvestAssignmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HarvestAssignmentLines_AppUsers_CheckedByUserId",
                        column: x => x.CheckedByUserId,
                        principalSchema: "be",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HarvestAssignmentLines_Crabs_CrabId",
                        column: x => x.CrabId,
                        principalSchema: "be",
                        principalTable: "Crabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HarvestAssignmentLines_HarvestAssignments_HarvestAssignment~",
                        column: x => x.HarvestAssignmentId,
                        principalSchema: "be",
                        principalTable: "HarvestAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HarvestVouchers_HarvestAssignmentId",
                schema: "be",
                table: "HarvestVouchers",
                column: "HarvestAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestLines_HarvestAssignmentLineId",
                schema: "be",
                table: "HarvestLines",
                column: "HarvestAssignmentLineId",
                unique: true,
                filter: "\"HarvestAssignmentLineId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestAssignmentLines_CheckedByUserId",
                schema: "be",
                table: "HarvestAssignmentLines",
                column: "CheckedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestAssignmentLines_CrabId",
                schema: "be",
                table: "HarvestAssignmentLines",
                column: "CrabId",
                unique: true,
                filter: "\"Status\" IN ('Assigned', 'Eligible', 'InProgress')");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestAssignmentLines_HarvestAssignmentId_CrabId",
                schema: "be",
                table: "HarvestAssignmentLines",
                columns: new[] { "HarvestAssignmentId", "CrabId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HarvestAssignments_AssignedByUserId",
                schema: "be",
                table: "HarvestAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestAssignments_AssignedToUserId",
                schema: "be",
                table: "HarvestAssignments",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestAssignments_AssignmentCode",
                schema: "be",
                table: "HarvestAssignments",
                column: "AssignmentCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HarvestAssignments_FarmingAreaId_AssignedToUserId_Status",
                schema: "be",
                table: "HarvestAssignments",
                columns: new[] { "FarmingAreaId", "AssignedToUserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_HarvestLines_HarvestAssignmentLines_HarvestAssignmentLineId",
                schema: "be",
                table: "HarvestLines",
                column: "HarvestAssignmentLineId",
                principalSchema: "be",
                principalTable: "HarvestAssignmentLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HarvestVouchers_HarvestAssignments_HarvestAssignmentId",
                schema: "be",
                table: "HarvestVouchers",
                column: "HarvestAssignmentId",
                principalSchema: "be",
                principalTable: "HarvestAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HarvestLines_HarvestAssignmentLines_HarvestAssignmentLineId",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropForeignKey(
                name: "FK_HarvestVouchers_HarvestAssignments_HarvestAssignmentId",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropTable(
                name: "HarvestAssignmentLines",
                schema: "be");

            migrationBuilder.DropTable(
                name: "HarvestAssignments",
                schema: "be");

            migrationBuilder.DropIndex(
                name: "IX_HarvestVouchers_HarvestAssignmentId",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropIndex(
                name: "IX_HarvestLines_HarvestAssignmentLineId",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropColumn(
                name: "HarvestAssignmentId",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "HarvestAssignmentLineId",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.CreateIndex(
                name: "IX_FarmStaffAssignments_ManagerUserId",
                schema: "be",
                table: "FarmStaffAssignments",
                column: "ManagerUserId");
        }
    }
}
