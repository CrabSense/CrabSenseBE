using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrabLotCloseSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AliveCountAtClose",
                schema: "be",
                table: "CrabLots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeadCountAtClose",
                schema: "be",
                table: "CrabLots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HarvestedCountAtClose",
                schema: "be",
                table: "CrabLots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MoltedCountAtClose",
                schema: "be",
                table: "CrabLots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RegisteredCountAtClose",
                schema: "be",
                table: "CrabLots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnstockedCountAtClose",
                schema: "be",
                table: "CrabLots",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AliveCountAtClose",
                schema: "be",
                table: "CrabLots");

            migrationBuilder.DropColumn(
                name: "DeadCountAtClose",
                schema: "be",
                table: "CrabLots");

            migrationBuilder.DropColumn(
                name: "HarvestedCountAtClose",
                schema: "be",
                table: "CrabLots");

            migrationBuilder.DropColumn(
                name: "MoltedCountAtClose",
                schema: "be",
                table: "CrabLots");

            migrationBuilder.DropColumn(
                name: "RegisteredCountAtClose",
                schema: "be",
                table: "CrabLots");

            migrationBuilder.DropColumn(
                name: "UnstockedCountAtClose",
                schema: "be",
                table: "CrabLots");
        }
    }
}
