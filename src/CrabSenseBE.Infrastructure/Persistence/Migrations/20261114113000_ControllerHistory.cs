using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261114113000_ControllerHistory")]
public sealed class ControllerHistory : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable(
            name: "ControllerEvents", schema: "be",
            columns: t => new
            {
                Id = t.Column<Guid>(type: "uuid", nullable: false),
                DeviceId = t.Column<Guid>(type: "uuid", nullable: false),
                EventKey = t.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Kind = t.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                InventoryRevision = t.Column<int>(type: "integer", nullable: false),
                Summary = t.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                DetailsJson = t.Column<string>(type: "text", nullable: true),
                CreatedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ControllerEvents", x => x.Id);
                t.ForeignKey("FK_ControllerEvents_Devices_DeviceId", x => x.DeviceId,
                    principalSchema: "be", principalTable: "Devices", principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        m.CreateIndex("IX_ControllerEvents_DeviceId_EventKey", "ControllerEvents",
            new[] { "DeviceId", "EventKey" }, schema: "be", unique: true);
        m.CreateIndex("IX_ControllerEvents_DeviceId_Kind_CreatedAt", "ControllerEvents",
            new[] { "DeviceId", "Kind", "CreatedAt" }, schema: "be");
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("ControllerEvents", "be");
}
