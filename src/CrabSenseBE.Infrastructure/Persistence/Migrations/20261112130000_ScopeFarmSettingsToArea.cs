using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    public partial class ScopeFarmSettingsToArea : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>("FarmingAreaId", "FarmSettings", schema: "be", type: "uuid", nullable: true);
            // No historical ownership evidence exists: preserve unassigned rows.
            migrationBuilder.CreateIndex("IX_FarmSettings_FarmingAreaId_Key", "FarmSettings",
                new[] { "FarmingAreaId", "Key" }, schema: "be", unique: true, filter: "\"FarmingAreaId\" IS NOT NULL");
            migrationBuilder.AddForeignKey("FK_FarmSettings_FarmingAreas_FarmingAreaId", "FarmSettings",
                "FarmingAreaId", "FarmingAreas", principalColumn: "Id", schema: "be", principalSchema: "be",
                onDelete: ReferentialAction.Restrict);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM be."FarmSettings" WHERE "FarmingAreaId" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot discard farm settings ownership; recover from verified backup.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey("FK_FarmSettings_FarmingAreas_FarmingAreaId", "FarmSettings", schema: "be");
            migrationBuilder.DropIndex("IX_FarmSettings_FarmingAreaId_Key", "FarmSettings", schema: "be");
            migrationBuilder.DropColumn("FarmingAreaId", "FarmSettings", schema: "be");
        }
    }
}
