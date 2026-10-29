using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>
/// Dùng FarmingAreas làm trang trại: thêm liên kết Admin–khu và gán một khu cho Manager/Staff.
/// Các khu hiện có của Farm Administrator được chuyển từ OwnerId sang bảng liên kết.
/// </summary>
public partial class UseFarmingAreasForUserAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "FarmingAreaId",
            schema: "be",
            table: "AppUsers",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "FarmingAreaAdministrators",
            schema: "be",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FarmingAreaAdministrators", x => new { x.UserId, x.FarmingAreaId });
                table.ForeignKey(
                    name: "FK_FarmingAreaAdministrators_AppUsers_UserId",
                    column: x => x.UserId,
                    principalSchema: "be",
                    principalTable: "AppUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_FarmingAreaAdministrators_FarmingAreas_FarmingAreaId",
                    column: x => x.FarmingAreaId,
                    principalSchema: "be",
                    principalTable: "FarmingAreas",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AppUsers_FarmingAreaId",
            schema: "be",
            table: "AppUsers",
            column: "FarmingAreaId");

        migrationBuilder.CreateIndex(
            name: "IX_FarmingAreaAdministrators_FarmingAreaId",
            schema: "be",
            table: "FarmingAreaAdministrators",
            column: "FarmingAreaId");

        // Giữ quyền quản lý của Farm Administrator đang là Owner của khu từ trước.
        migrationBuilder.Sql("""
            INSERT INTO be."FarmingAreaAdministrators" ("UserId", "FarmingAreaId")
            SELECT area."OwnerId", area."Id"
            FROM be."FarmingAreas" AS area
            INNER JOIN be."AppUsers" AS admin ON admin."Id" = area."OwnerId"
            WHERE admin."Role" IN ('FarmAdministrator', 'FarmOwner')
            ON CONFLICT ("UserId", "FarmingAreaId") DO NOTHING;
            """);

        migrationBuilder.AddForeignKey(
            name: "FK_AppUsers_FarmingAreas_FarmingAreaId",
            schema: "be",
            table: "AppUsers",
            column: "FarmingAreaId",
            principalSchema: "be",
            principalTable: "FarmingAreas",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_AppUsers_FarmingAreas_FarmingAreaId",
            schema: "be",
            table: "AppUsers");

        migrationBuilder.DropTable(
            name: "FarmingAreaAdministrators",
            schema: "be");

        migrationBuilder.DropIndex(
            name: "IX_AppUsers_FarmingAreaId",
            schema: "be",
            table: "AppUsers");

        migrationBuilder.DropColumn(
            name: "FarmingAreaId",
            schema: "be",
            table: "AppUsers");
    }
}
