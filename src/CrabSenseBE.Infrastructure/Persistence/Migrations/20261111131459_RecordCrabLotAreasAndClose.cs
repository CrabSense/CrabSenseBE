using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordCrabLotAreasAndClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloseDisposition",
                schema: "be",
                table: "CrabLots",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                schema: "be",
                table: "CrabLots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StockedAt",
                schema: "be",
                table: "CrabLots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnmoltedCountAtClose",
                schema: "be",
                table: "CrabLots",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CrabLotAreas",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CrabLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrabLotAreas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrabLotAreas_CrabLots_CrabLotId",
                        column: x => x.CrabLotId,
                        principalSchema: "be",
                        principalTable: "CrabLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CrabLotAreas_FarmingAreas_FarmingAreaId",
                        column: x => x.FarmingAreaId,
                        principalSchema: "be",
                        principalTable: "FarmingAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrabLotAreas_CrabLotId_FarmingAreaId",
                schema: "be",
                table: "CrabLotAreas",
                columns: new[] { "CrabLotId", "FarmingAreaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrabLotAreas_FarmingAreaId",
                schema: "be",
                table: "CrabLotAreas",
                column: "FarmingAreaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrabLotAreas",
                schema: "be");

            migrationBuilder.DropColumn(
                name: "CloseDisposition",
                schema: "be",
                table: "CrabLots");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                schema: "be",
                table: "CrabLots");

            migrationBuilder.DropColumn(
                name: "StockedAt",
                schema: "be",
                table: "CrabLots");

            migrationBuilder.DropColumn(
                name: "UnmoltedCountAtClose",
                schema: "be",
                table: "CrabLots");
        }
    }
}
