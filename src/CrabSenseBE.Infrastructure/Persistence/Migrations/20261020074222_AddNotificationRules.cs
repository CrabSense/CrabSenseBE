using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId",
                schema: "be",
                table: "Notifications");

            migrationBuilder.AddColumn<string>(
                name: "TypeCode",
                schema: "be",
                table: "Notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationRules",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TypeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MinSeverity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RecipientRoles = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ScopeByArea = table.Column<bool>(type: "boolean", nullable: false),
                    ChannelCodes = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TitleTemplate = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BodyTemplate = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_TypeCode",
                schema: "be",
                table: "Notifications",
                columns: new[] { "UserId", "TypeCode" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_TypeCode",
                schema: "be",
                table: "NotificationRules",
                column: "TypeCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationRules",
                schema: "be");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_TypeCode",
                schema: "be",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "TypeCode",
                schema: "be",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId",
                schema: "be",
                table: "Notifications",
                column: "UserId");
        }
    }
}
