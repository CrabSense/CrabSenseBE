using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenEdgeCommandDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryAttemptId",
                schema: "be",
                table: "EdgeCommands",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                schema: "be",
                table: "EdgeCommands",
                type: "timestamp with time zone",
                nullable: true);
            migrationBuilder.Sql(
                """
                UPDATE be."EdgeCommands"
                SET "ExpiresAt" = COALESCE("CreatedAt", now()) + interval '5 minutes'
                WHERE "ExpiresAt" IS NULL;
                """);
            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAt",
                schema: "be",
                table: "EdgeCommands",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryAttemptId",
                schema: "be",
                table: "EdgeCommands");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                schema: "be",
                table: "EdgeCommands");
        }
    }
}
