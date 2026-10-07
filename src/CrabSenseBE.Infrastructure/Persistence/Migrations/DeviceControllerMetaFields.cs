using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CrabSenseBE.Infrastructure.Persistence;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>Controller metadata: vị trí lắp đặt + ghi chú quản lý.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260924100000_DeviceControllerMetaFields")]
public partial class DeviceControllerMetaFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."Devices"
            ADD COLUMN IF NOT EXISTS "InstallationLocation" character varying(200) NULL,
            ADD COLUMN IF NOT EXISTS "Notes" character varying(500) NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."Devices"
            DROP COLUMN IF EXISTS "InstallationLocation",
            DROP COLUMN IF EXISTS "Notes";
            """);
    }
}
