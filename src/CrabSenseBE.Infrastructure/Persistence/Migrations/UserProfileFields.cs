using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class UserProfileFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Phone",
            schema: "be",
            table: "AppUsers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EmployeeId",
            schema: "be",
            table: "AppUsers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AvatarUrl",
            schema: "be",
            table: "AppUsers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NotificationPrefsJson",
            schema: "be",
            table: "AppUsers",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Phone",
            schema: "be",
            table: "AppUsers");

        migrationBuilder.DropColumn(
            name: "EmployeeId",
            schema: "be",
            table: "AppUsers");

        migrationBuilder.DropColumn(
            name: "AvatarUrl",
            schema: "be",
            table: "AppUsers");

        migrationBuilder.DropColumn(
            name: "NotificationPrefsJson",
            schema: "be",
            table: "AppUsers");
    }
}
