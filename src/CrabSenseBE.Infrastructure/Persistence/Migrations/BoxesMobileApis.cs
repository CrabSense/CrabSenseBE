using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoxesMobileApis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BoxId",
                schema: "be",
                table: "Inspections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MoltingStatus",
                schema: "be",
                table: "Inspections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HealthStatus",
                schema: "be",
                table: "Inspections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightGram",
                schema: "be",
                table: "Inspections",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedMediaId",
                schema: "be",
                table: "Inspections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrlsJson",
                schema: "be",
                table: "Inspections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorName",
                schema: "be",
                table: "Inspections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiAgreement",
                schema: "be",
                table: "Inspections",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BoxId",
                schema: "be",
                table: "AiDetections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaId",
                schema: "be",
                table: "AiDetections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "be",
                table: "AiDetections",
                type: "text",
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.CreateTable(
                name: "FarmOperations",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    BoxIdsJson = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    Unit = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    PhotoUrlsJson = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FarmOperations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FarmOperations", schema: "be");

            migrationBuilder.DropColumn(name: "BoxId", schema: "be", table: "Inspections");
            migrationBuilder.DropColumn(name: "MoltingStatus", schema: "be", table: "Inspections");
            migrationBuilder.DropColumn(name: "HealthStatus", schema: "be", table: "Inspections");
            migrationBuilder.DropColumn(name: "WeightGram", schema: "be", table: "Inspections");
            migrationBuilder.DropColumn(name: "RelatedMediaId", schema: "be", table: "Inspections");
            migrationBuilder.DropColumn(name: "PhotoUrlsJson", schema: "be", table: "Inspections");
            migrationBuilder.DropColumn(name: "OperatorName", schema: "be", table: "Inspections");
            migrationBuilder.DropColumn(name: "AiAgreement", schema: "be", table: "Inspections");

            migrationBuilder.DropColumn(name: "BoxId", schema: "be", table: "AiDetections");
            migrationBuilder.DropColumn(name: "MediaId", schema: "be", table: "AiDetections");
            migrationBuilder.DropColumn(name: "Status", schema: "be", table: "AiDetections");
        }
    }
}
