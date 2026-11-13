using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113192000_SensorCodeUniqueness")]
public sealed class SensorCodeUniquenessMigration : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM be."Sensors" GROUP BY "SensorCode" HAVING count(*) > 1) THEN
                RAISE EXCEPTION 'Duplicate SensorCode values exist; reconcile sensor ownership before applying uniqueness migration.';
            END IF;
        END $$;
        CREATE UNIQUE INDEX "IX_Sensors_SensorCode" ON be."Sensors" ("SensorCode");
        """);
    protected override void Down(MigrationBuilder m) => m.Sql("""DROP INDEX be."IX_Sensors_SensorCode";""");
}
