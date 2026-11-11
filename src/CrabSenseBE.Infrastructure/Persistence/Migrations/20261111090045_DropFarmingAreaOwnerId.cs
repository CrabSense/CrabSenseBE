using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropFarmingAreaOwnerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Chủ khu là Farm Administrator nhưng chưa có dòng gán thì chuyển sang bảng trung gian.
            // Khu đã có admin giữ nguyên. Chủ không phải Farm Administrator không được thêm.
            migrationBuilder.Sql("""
                INSERT INTO be."FarmingAreaAdministrators" ("UserId", "FarmingAreaId")
                SELECT area."OwnerId", area."Id"
                FROM be."FarmingAreas" AS area
                INNER JOIN be."AppUsers" AS admin ON admin."Id" = area."OwnerId"
                WHERE admin."Role" IN ('FarmAdministrator', 'FarmOwner')
                  AND NOT EXISTS (
                    SELECT 1
                    FROM be."FarmingAreaAdministrators" AS link
                    WHERE link."FarmingAreaId" = area."Id")
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_FarmingAreas_AppUsers_OwnerId",
                schema: "be",
                table: "FarmingAreas");

            migrationBuilder.DropIndex(
                name: "IX_FarmingAreas_OwnerId",
                schema: "be",
                table: "FarmingAreas");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "be",
                table: "FarmingAreas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                schema: "be",
                table: "FarmingAreas",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_FarmingAreas_OwnerId",
                schema: "be",
                table: "FarmingAreas",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_FarmingAreas_AppUsers_OwnerId",
                schema: "be",
                table: "FarmingAreas",
                column: "OwnerId",
                principalSchema: "be",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
