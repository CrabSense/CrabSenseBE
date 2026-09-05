using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FarmingAreaProfile : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Code",
            schema: "be",
            table: "FarmingAreas",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Address",
            schema: "be",
            table: "FarmingAreas",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Region",
            schema: "be",
            table: "FarmingAreas",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "AreaSquareMeters",
            schema: "be",
            table: "FarmingAreas",
            type: "numeric(12,2)",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "EstablishedAt",
            schema: "be",
            table: "FarmingAreas",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AvatarUrl",
            schema: "be",
            table: "FarmingAreas",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Status",
            schema: "be",
            table: "FarmingAreas",
            type: "text",
            nullable: false,
            defaultValue: "Active");

        migrationBuilder.Sql(
            """
            UPDATE be."FarmingAreas"
            SET
                "Address" = CASE
                    WHEN "Address" IS NULL OR btrim("Address") = '' THEN 'Chưa cập nhật'
                    ELSE "Address"
                END,
                "Status" = CASE WHEN "IsActive" THEN 'Active' ELSE 'Closed' END
            WHERE "Code" IS NULL OR btrim("Code") = '';

            WITH numbered AS (
                SELECT "Id", ROW_NUMBER() OVER (ORDER BY "CreatedAt", "Id") AS n
                FROM be."FarmingAreas"
                WHERE "Code" IS NULL OR btrim("Code") = ''
            )
            UPDATE be."FarmingAreas" a
            SET "Code" = 'FARM-' || LPAD(numbered.n::text, 3, '0')
            FROM numbered
            WHERE a."Id" = numbered."Id";
            """);

        migrationBuilder.CreateIndex(
            name: "IX_FarmingAreas_Code",
            schema: "be",
            table: "FarmingAreas",
            column: "Code",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_FarmingAreas_Code",
            schema: "be",
            table: "FarmingAreas");

        migrationBuilder.DropColumn(name: "Code", schema: "be", table: "FarmingAreas");
        migrationBuilder.DropColumn(name: "Address", schema: "be", table: "FarmingAreas");
        migrationBuilder.DropColumn(name: "Region", schema: "be", table: "FarmingAreas");
        migrationBuilder.DropColumn(name: "AreaSquareMeters", schema: "be", table: "FarmingAreas");
        migrationBuilder.DropColumn(name: "EstablishedAt", schema: "be", table: "FarmingAreas");
        migrationBuilder.DropColumn(name: "AvatarUrl", schema: "be", table: "FarmingAreas");
        migrationBuilder.DropColumn(name: "Status", schema: "be", table: "FarmingAreas");
    }
}
