using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeSyncIdempotencyByActor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SyncInboxItems_IdempotencyKey",
                schema: "be",
                table: "SyncInboxItems");

            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                schema: "be",
                table: "SyncInboxItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                schema: "be",
                table: "SyncInboxItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SyncInboxItems_ActorUserId_IdempotencyKey",
                schema: "be",
                table: "SyncInboxItems",
                columns: new[] { "ActorUserId", "IdempotencyKey" },
                unique: true,
                filter: "\"ActorUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SyncInboxItems_IdempotencyKey",
                schema: "be",
                table: "SyncInboxItems",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"ActorUserId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SyncInboxItems_ActorUserId_IdempotencyKey",
                schema: "be",
                table: "SyncInboxItems");

            migrationBuilder.DropIndex(
                name: "IX_SyncInboxItems_IdempotencyKey",
                schema: "be",
                table: "SyncInboxItems");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                schema: "be",
                table: "SyncInboxItems");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                schema: "be",
                table: "SyncInboxItems");

            migrationBuilder.CreateIndex(
                name: "IX_SyncInboxItems_IdempotencyKey",
                schema: "be",
                table: "SyncInboxItems",
                column: "IdempotencyKey",
                unique: true);
        }
    }
}
