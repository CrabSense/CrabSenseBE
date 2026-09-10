using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoxStatusHistoryAndFarmingTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BoxStatusHistories",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoxId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldStatus = table.Column<string>(type: "text", nullable: true),
                    NewStatus = table.Column<string>(type: "text", nullable: false),
                    OldIsOccupied = table.Column<bool>(type: "boolean", nullable: true),
                    NewIsOccupied = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoxStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoxStatusHistories_AppUsers_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalSchema: "be",
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BoxStatusHistories_Boxes_BoxId",
                        column: x => x.BoxId,
                        principalSchema: "be",
                        principalTable: "Boxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoxStatusHistories_BoxId",
                schema: "be",
                table: "BoxStatusHistories",
                column: "BoxId");

            migrationBuilder.CreateIndex(
                name: "IX_BoxStatusHistories_ChangedByUserId",
                schema: "be",
                table: "BoxStatusHistories",
                column: "ChangedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoxStatusHistories",
                schema: "be");
        }
    }
}
