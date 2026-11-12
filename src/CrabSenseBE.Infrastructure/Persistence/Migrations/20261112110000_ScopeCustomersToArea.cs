using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    public partial class ScopeCustomersToArea : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FarmingAreaId", schema: "be", table: "Customers",
                type: "uuid", nullable: true);
            // Only infer ownership when every existing order has the same valid area.
            // Preserve ambiguous/standalone legacy customers without guessing or deleting.
            migrationBuilder.Sql("""
                UPDATE be."Customers" c SET "FarmingAreaId" = ownership.area_id
                FROM (
                    SELECT o."CustomerId", (array_agg(DISTINCT o."FarmingAreaId"))[1] AS area_id
                    FROM be."SalesOrders" o
                    LEFT JOIN be."FarmingAreas" a ON a."Id" = o."FarmingAreaId"
                    GROUP BY o."CustomerId"
                    HAVING count(DISTINCT o."FarmingAreaId") = 1
                        AND count(*) = count(a."Id")
                ) ownership
                WHERE c."Id" = ownership."CustomerId";
                """);
            migrationBuilder.CreateIndex(name: "IX_Customers_FarmingAreaId",
                schema: "be", table: "Customers", column: "FarmingAreaId");
            migrationBuilder.AddForeignKey(name: "FK_Customers_FarmingAreas_FarmingAreaId",
                schema: "be", table: "Customers", column: "FarmingAreaId",
                principalSchema: "be", principalTable: "FarmingAreas", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM be."Customers" WHERE "FarmingAreaId" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot discard customer area ownership; recover using a verified backup.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(name: "FK_Customers_FarmingAreas_FarmingAreaId",
                schema: "be", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_FarmingAreaId", schema: "be", table: "Customers");
            migrationBuilder.DropColumn(name: "FarmingAreaId", schema: "be", table: "Customers");
        }
    }
}
