using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBoxIdToHarvestLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BoxId",
                schema: "be",
                table: "HarvestLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HarvestLines_BoxId",
                schema: "be",
                table: "HarvestLines",
                column: "BoxId");

            migrationBuilder.AddForeignKey(
                name: "FK_HarvestLines_Boxes_BoxId",
                schema: "be",
                table: "HarvestLines",
                column: "BoxId",
                principalSchema: "be",
                principalTable: "Boxes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HarvestLines_Boxes_BoxId",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropIndex(
                name: "IX_HarvestLines_BoxId",
                schema: "be",
                table: "HarvestLines");

            migrationBuilder.DropColumn(
                name: "BoxId",
                schema: "be",
                table: "HarvestLines");
        }
    }
}
