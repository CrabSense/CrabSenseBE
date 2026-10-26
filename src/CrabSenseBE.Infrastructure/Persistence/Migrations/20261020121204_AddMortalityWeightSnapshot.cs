using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMortalityWeightSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BoxId",
                schema: "be",
                table: "CrabMortalityRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CarapaceLengthMm",
                schema: "be",
                table: "CrabMortalityRecords",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CarapaceWidthMm",
                schema: "be",
                table: "CrabMortalityRecords",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightGram",
                schema: "be",
                table: "CrabMortalityRecords",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrabMortalityRecords_BoxId",
                schema: "be",
                table: "CrabMortalityRecords",
                column: "BoxId");

            migrationBuilder.AddForeignKey(
                name: "FK_CrabMortalityRecords_Boxes_BoxId",
                schema: "be",
                table: "CrabMortalityRecords",
                column: "BoxId",
                principalSchema: "be",
                principalTable: "Boxes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CrabMortalityRecords_Boxes_BoxId",
                schema: "be",
                table: "CrabMortalityRecords");

            migrationBuilder.DropIndex(
                name: "IX_CrabMortalityRecords_BoxId",
                schema: "be",
                table: "CrabMortalityRecords");

            migrationBuilder.DropColumn(
                name: "BoxId",
                schema: "be",
                table: "CrabMortalityRecords");

            migrationBuilder.DropColumn(
                name: "CarapaceLengthMm",
                schema: "be",
                table: "CrabMortalityRecords");

            migrationBuilder.DropColumn(
                name: "CarapaceWidthMm",
                schema: "be",
                table: "CrabMortalityRecords");

            migrationBuilder.DropColumn(
                name: "WeightGram",
                schema: "be",
                table: "CrabMortalityRecords");
        }
    }
}
