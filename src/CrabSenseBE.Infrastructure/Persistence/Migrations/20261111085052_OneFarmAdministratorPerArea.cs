using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneFarmAdministratorPerArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mỗi khu giữ một Farm Administrator: ưu tiên user trùng OwnerId.
            migrationBuilder.Sql("""
                DELETE FROM be."FarmingAreaAdministrators" AS extra
                USING (
                    SELECT link."UserId",
                           link."FarmingAreaId",
                           ROW_NUMBER() OVER (
                               PARTITION BY link."FarmingAreaId"
                               ORDER BY CASE WHEN link."UserId" = area."OwnerId" THEN 0 ELSE 1 END,
                                        link."UserId"
                           ) AS rn
                    FROM be."FarmingAreaAdministrators" AS link
                    INNER JOIN be."FarmingAreas" AS area ON area."Id" = link."FarmingAreaId"
                ) AS ranked
                WHERE extra."UserId" = ranked."UserId"
                  AND extra."FarmingAreaId" = ranked."FarmingAreaId"
                  AND ranked.rn > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_FarmingAreaAdministrators_FarmingAreaId",
                schema: "be",
                table: "FarmingAreaAdministrators");

            migrationBuilder.CreateIndex(
                name: "IX_FarmingAreaAdministrators_FarmingAreaId",
                schema: "be",
                table: "FarmingAreaAdministrators",
                column: "FarmingAreaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FarmingAreaAdministrators_FarmingAreaId",
                schema: "be",
                table: "FarmingAreaAdministrators");

            migrationBuilder.CreateIndex(
                name: "IX_FarmingAreaAdministrators_FarmingAreaId",
                schema: "be",
                table: "FarmingAreaAdministrators",
                column: "FarmingAreaId");
        }
    }
}
