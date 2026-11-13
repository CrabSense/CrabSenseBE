using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113191000_SensorDiagnosticRuns")]
public sealed class SensorDiagnosticRunsMigration : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE be."SensorDiagnosticRuns" (
            "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz,
            "SensorId" uuid NOT NULL CONSTRAINT "FK_SensorDiagnosticRuns_Sensors_SensorId" REFERENCES be."Sensors"("Id") ON DELETE RESTRICT,
            "DeviceId" uuid NOT NULL, "RequestedBy" uuid NOT NULL, "ConfigurationVersion" integer NOT NULL,
            "Status" text NOT NULL, "ExpiresAt" timestamptz NOT NULL, "CompletedAt" timestamptz, "MeasuredAt" timestamptz,
            "Value" numeric, "Unit" text, "ResultMessage" text
        );
        CREATE UNIQUE INDEX "IX_SensorDiagnosticRuns_SensorId" ON be."SensorDiagnosticRuns" ("SensorId") WHERE "Status" = 'Pending';
        CREATE INDEX "IX_SensorDiagnosticRuns_SensorId_CreatedAt" ON be."SensorDiagnosticRuns" ("SensorId","CreatedAt");
        CREATE INDEX "IX_SensorDiagnosticRuns_DeviceId_Status_ExpiresAt" ON be."SensorDiagnosticRuns" ("DeviceId","Status","ExpiresAt");
        """);
    protected override void Down(MigrationBuilder m) => m.Sql("""DROP TABLE be."SensorDiagnosticRuns";""");
}
