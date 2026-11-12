using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertIncidentIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FarmingAreaId",
                schema: "be",
                table: "Alerts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncidentType",
                schema: "be",
                table: "Alerts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                schema: "be",
                table: "Alerts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                schema: "be",
                table: "Alerts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_FarmingAreaId",
                schema: "be",
                table: "Alerts",
                column: "FarmingAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_FarmingAreaId_SourceType_SourceId_IncidentType",
                schema: "be",
                table: "Alerts",
                columns: new[] { "FarmingAreaId", "SourceType", "SourceId", "IncidentType" },
                unique: true,
                filter: "\"SourceType\" IS NOT NULL AND \"SourceId\" IS NOT NULL AND \"IncidentType\" IS NOT NULL AND \"Status\" IN ('Active', 'Acknowledged', 'InProgress')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Alerts_FarmingAreaId",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_FarmingAreaId_SourceType_SourceId_IncidentType",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "FarmingAreaId",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "IncidentType",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "SourceId",
                schema: "be",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "SourceType",
                schema: "be",
                table: "Alerts");
        }
    }
}
