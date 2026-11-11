using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditCrabEventCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                schema: "be",
                table: "MoltingRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                schema: "be",
                table: "MoltingRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                schema: "be",
                table: "MoltingRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "be",
                table: "MoltingRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CrabEventCorrections",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CrabId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PreviousDataJson = table.Column<string>(type: "text", nullable: false),
                    NewDataJson = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CorrectedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrabEventCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrabEventCorrections_Crabs_CrabId",
                        column: x => x.CrabId,
                        principalSchema: "be",
                        principalTable: "Crabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrabEventCorrections_CrabId",
                schema: "be",
                table: "CrabEventCorrections",
                column: "CrabId");

            migrationBuilder.CreateIndex(
                name: "IX_CrabEventCorrections_EventType_EventId_CorrectedAt",
                schema: "be",
                table: "CrabEventCorrections",
                columns: new[] { "EventType", "EventId", "CorrectedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrabEventCorrections",
                schema: "be");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "be",
                table: "MoltingRecords");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "be",
                table: "MoltingRecords");
        }
    }
}
