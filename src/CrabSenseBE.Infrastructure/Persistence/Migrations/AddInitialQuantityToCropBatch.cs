using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInitialQuantityToCropBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InitialQuantity",
                schema: "be",
                table: "CropBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CrabMortalityRecords",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CrabId = table.Column<Guid>(type: "uuid", nullable: false),
                    MortalityDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Cause = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecorderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CropBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrabMortalityRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrabMortalityRecords_AppUsers_RecorderId",
                        column: x => x.RecorderId,
                        principalSchema: "be",
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CrabMortalityRecords_Crabs_CrabId",
                        column: x => x.CrabId,
                        principalSchema: "be",
                        principalTable: "Crabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CrabMortalityRecords_CropBatches_CropBatchId",
                        column: x => x.CropBatchId,
                        principalSchema: "be",
                        principalTable: "CropBatches",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrabMortalityRecords_CrabId",
                schema: "be",
                table: "CrabMortalityRecords",
                column: "CrabId");

            migrationBuilder.CreateIndex(
                name: "IX_CrabMortalityRecords_CropBatchId",
                schema: "be",
                table: "CrabMortalityRecords",
                column: "CropBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CrabMortalityRecords_RecorderId",
                schema: "be",
                table: "CrabMortalityRecords",
                column: "RecorderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrabMortalityRecords",
                schema: "be");

            migrationBuilder.DropColumn(
                name: "InitialQuantity",
                schema: "be",
                table: "CropBatches");
        }
    }
}
