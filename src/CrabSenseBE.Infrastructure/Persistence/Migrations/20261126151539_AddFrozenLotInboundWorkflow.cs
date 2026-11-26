using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFrozenLotInboundWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOpenForInbound",
                schema: "be",
                table: "FrozenLots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "Grade",
                schema: "be",
                table: "FrozenCrabItems",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1)",
                oldMaxLength: 1);

            migrationBuilder.CreateIndex(
                name: "IX_FrozenLots_OneOpenPerArea",
                schema: "be",
                table: "FrozenLots",
                column: "FarmingAreaId",
                unique: true,
                filter: "\"IsOpenForInbound\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FrozenLots_OneOpenPerArea",
                schema: "be",
                table: "FrozenLots");

            migrationBuilder.DropColumn(
                name: "IsOpenForInbound",
                schema: "be",
                table: "FrozenLots");

            migrationBuilder.AlterColumn<string>(
                name: "Grade",
                schema: "be",
                table: "FrozenCrabItems",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

        }
    }
}
