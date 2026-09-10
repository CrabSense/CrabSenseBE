using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCrabSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CrabMortalityRecords_CropBatches_CropBatchId",
                schema: "be",
                table: "CrabMortalityRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_Crabs_Boxes_BoxId",
                schema: "be",
                table: "Crabs");

            migrationBuilder.DropForeignKey(
                name: "FK_Crabs_CropBatches_CropBatchId",
                schema: "be",
                table: "Crabs");

            migrationBuilder.DropTable(
                name: "CropBatches",
                schema: "be");

            migrationBuilder.DropIndex(
                name: "IX_Crabs_CropBatchId",
                schema: "be",
                table: "Crabs");

            migrationBuilder.DropIndex(
                name: "IX_CrabMortalityRecords_CropBatchId",
                schema: "be",
                table: "CrabMortalityRecords");

            migrationBuilder.DropColumn(
                name: "CropBatchId",
                schema: "be",
                table: "HarvestVouchers");

            migrationBuilder.DropColumn(
                name: "CropBatchId",
                schema: "be",
                table: "Crabs");

            migrationBuilder.DropColumn(
                name: "IsAlive",
                schema: "be",
                table: "Crabs");

            migrationBuilder.DropColumn(
                name: "CropBatchId",
                schema: "be",
                table: "CrabMortalityRecords");

            migrationBuilder.AlterColumn<string>(
                name: "MoltingStage",
                schema: "be",
                table: "Crabs",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "BoxId",
                schema: "be",
                table: "Crabs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "be",
                table: "Crabs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StockedAt",
                schema: "be",
                table: "Crabs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddForeignKey(
                name: "FK_Crabs_Boxes_BoxId",
                schema: "be",
                table: "Crabs",
                column: "BoxId",
                principalSchema: "be",
                principalTable: "Boxes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Crabs_Boxes_BoxId",
                schema: "be",
                table: "Crabs");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "be",
                table: "Crabs");

            migrationBuilder.DropColumn(
                name: "StockedAt",
                schema: "be",
                table: "Crabs");

            migrationBuilder.AddColumn<Guid>(
                name: "CropBatchId",
                schema: "be",
                table: "HarvestVouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MoltingStage",
                schema: "be",
                table: "Crabs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "BoxId",
                schema: "be",
                table: "Crabs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CropBatchId",
                schema: "be",
                table: "Crabs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "IsAlive",
                schema: "be",
                table: "Crabs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CropBatchId",
                schema: "be",
                table: "CrabMortalityRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CropBatches",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchCode = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InitialQuantity = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CropBatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Crabs_CropBatchId",
                schema: "be",
                table: "Crabs",
                column: "CropBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CrabMortalityRecords_CropBatchId",
                schema: "be",
                table: "CrabMortalityRecords",
                column: "CropBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_CrabMortalityRecords_CropBatches_CropBatchId",
                schema: "be",
                table: "CrabMortalityRecords",
                column: "CropBatchId",
                principalSchema: "be",
                principalTable: "CropBatches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Crabs_Boxes_BoxId",
                schema: "be",
                table: "Crabs",
                column: "BoxId",
                principalSchema: "be",
                principalTable: "Boxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Crabs_CropBatches_CropBatchId",
                schema: "be",
                table: "Crabs",
                column: "CropBatchId",
                principalSchema: "be",
                principalTable: "CropBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
