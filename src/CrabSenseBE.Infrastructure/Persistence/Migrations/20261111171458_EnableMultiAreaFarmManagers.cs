using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableMultiAreaFarmManagers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FarmManagerAssignments_ManagerUserId",
                schema: "be",
                table: "FarmManagerAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_FarmManagerAssignments_ManagerUserId_FarmingAreaId",
                schema: "be",
                table: "FarmManagerAssignments",
                columns: new[] { "ManagerUserId", "FarmingAreaId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FarmManagerAssignments_ManagerUserId_FarmingAreaId",
                schema: "be",
                table: "FarmManagerAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_FarmManagerAssignments_ManagerUserId",
                schema: "be",
                table: "FarmManagerAssignments",
                column: "ManagerUserId",
                unique: true);
        }
    }
}
