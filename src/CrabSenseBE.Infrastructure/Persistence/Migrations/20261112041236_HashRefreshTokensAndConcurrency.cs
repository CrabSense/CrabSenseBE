using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HashRefreshTokensAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RefreshTokenHash",
                schema: "be",
                table: "AppUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefreshTokenVersion",
                schema: "be",
                table: "AppUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_RefreshTokenHash",
                schema: "be",
                table: "AppUsers",
                column: "RefreshTokenHash",
                unique: true,
                filter: "\"RefreshTokenHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_RefreshTokenHash",
                schema: "be",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "RefreshTokenHash",
                schema: "be",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "RefreshTokenVersion",
                schema: "be",
                table: "AppUsers");
        }
    }
}
