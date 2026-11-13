using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113190000_SensorManagement")]
public sealed class SensorManagementMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.Sql("""
            ALTER TABLE be."Sensors" ADD COLUMN "Name" text, ADD COLUMN "InstallationLocation" text,
                ADD COLUMN "FarmingAreaId" uuid REFERENCES be."FarmingAreas"("Id") ON DELETE RESTRICT,
                ADD COLUMN "SamplingIntervalSeconds" integer, ADD COLUMN "DisconnectTimeoutSeconds" integer,
                ADD COLUMN "SourceType" text, ADD COLUMN "BusPortId" uuid REFERENCES be."ControllerPorts"("Id") ON DELETE RESTRICT,
                ADD COLUMN "BusAddress" text, ADD COLUMN "BusRegister" text,
                ADD COLUMN "ConfigurationVersion" integer NOT NULL DEFAULT 0,
                ADD COLUMN "AppliedConfigurationVersion" integer, ADD COLUMN "LastConfigurationAppliedAt" timestamptz;
            CREATE INDEX "IX_Sensors_FarmingAreaId" ON be."Sensors" ("FarmingAreaId");
            CREATE UNIQUE INDEX "IX_Sensors_BusPortId_BusAddress_BusRegister" ON be."Sensors" ("BusPortId","BusAddress","BusRegister");
            ALTER TABLE be."WaterMeasurements" ADD COLUMN "ReceivedAt" timestamptz, ADD COLUMN "DataQuality" text;
            CREATE TABLE be."SensorCalibrations" (
                "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz,
                "SensorId" uuid NOT NULL REFERENCES be."Sensors"("Id") ON DELETE RESTRICT,
                "ActorId" uuid NOT NULL, "PerformedAt" timestamptz NOT NULL,
                "ReferenceValue" numeric NOT NULL, "ObservedValue" numeric NOT NULL,
                "Unit" text NOT NULL, "Method" text NOT NULL, "Result" text NOT NULL, "Note" text
            );
            CREATE INDEX "IX_SensorCalibrations_SensorId_PerformedAt" ON be."SensorCalibrations" ("SensorId","PerformedAt");
            """);
    }
    protected override void Down(MigrationBuilder m) => m.Sql("""
        DROP TABLE be."SensorCalibrations";
        ALTER TABLE be."WaterMeasurements" DROP COLUMN "ReceivedAt", DROP COLUMN "DataQuality";
        ALTER TABLE be."Sensors" DROP COLUMN "Name", DROP COLUMN "InstallationLocation", DROP COLUMN "FarmingAreaId",
            DROP COLUMN "SamplingIntervalSeconds", DROP COLUMN "DisconnectTimeoutSeconds", DROP COLUMN "SourceType",
            DROP COLUMN "BusPortId", DROP COLUMN "BusAddress", DROP COLUMN "BusRegister",
            DROP COLUMN "ConfigurationVersion", DROP COLUMN "AppliedConfigurationVersion", DROP COLUMN "LastConfigurationAppliedAt";
        """);
}
