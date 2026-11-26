using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261126090000_WaterBenchEvidence")]
public sealed class AddWaterBenchEvidence : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE be."WaterBenchStations" (
            "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
            "KioskId" uuid NOT NULL REFERENCES be."FarmKiosks"("Id") ON DELETE RESTRICT,
            "MasterHardwareId" varchar(12) NOT NULL, "SlaveHardwareId" varchar(12) NOT NULL,
            "RevokedAt" timestamptz NULL,
            CONSTRAINT "CK_WaterBenchStation_Hardware" CHECK ("MasterHardwareId" ~ '^[0-9a-f]{12}$' AND "SlaveHardwareId" ~ '^[0-9a-f]{12}$' AND "MasterHardwareId" <> "SlaveHardwareId")
        );
        CREATE UNIQUE INDEX "IX_WaterBenchStations_KioskId" ON be."WaterBenchStations"("KioskId") WHERE "RevokedAt" IS NULL;
        CREATE UNIQUE INDEX "IX_WaterBenchStations_MasterHardwareId" ON be."WaterBenchStations"("MasterHardwareId") WHERE "RevokedAt" IS NULL;
        CREATE UNIQUE INDEX "IX_WaterBenchStations_SlaveHardwareId" ON be."WaterBenchStations"("SlaveHardwareId") WHERE "RevokedAt" IS NULL;
        CREATE TABLE be."WaterBenchEvidence" (
            "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
            "StationId" uuid NOT NULL REFERENCES be."WaterBenchStations"("Id") ON DELETE RESTRICT,
            "EventId" uuid NOT NULL, "ConnectionId" uuid NOT NULL, "Kind" varchar(32) NOT NULL,
            "BootId" varchar(16) NULL, "FirmwareBatchId" varchar(16) NULL, "FirmwareSessionId" varchar(18) NULL,
            "RequestId" uuid NULL, "CaptureId" integer NULL, "Analyte" varchar(3) NULL, "KioskReceivedAt" timestamptz NULL,
            "PayloadSha256" varchar(64) NOT NULL, "PayloadJson" text NOT NULL,
            CONSTRAINT "CK_WaterBenchEvidence_Identity" CHECK ("EventId" <> '00000000-0000-0000-0000-000000000000' AND "ConnectionId" <> '00000000-0000-0000-0000-000000000000'),
            CONSTRAINT "CK_WaterBenchEvidence_Correlation" CHECK (("BootId" IS NULL OR "BootId" ~ '^[0-9a-f]{16}$') AND ("FirmwareBatchId" IS NULL OR "FirmwareBatchId" ~ '^[0-9a-f]{16}$') AND ("FirmwareSessionId" IS NULL OR "FirmwareSessionId" ~ '^[0-9a-f]{16}(-[12])?$') AND ("CaptureId" IS NULL OR "CaptureId" BETWEEN 1 AND 65536) AND ("Analyte" IS NULL OR "Analyte" IN ('NO2','NO3','NH3'))),
            CONSTRAINT "CK_WaterBenchEvidence_Payload" CHECK ("PayloadSha256" ~ '^[0-9a-f]{64}$' AND octet_length("PayloadJson") BETWEEN 2 AND 65536 AND jsonb_typeof("PayloadJson"::jsonb) = 'object'),
            CONSTRAINT "CK_WaterBenchEvidence_Kind" CHECK ("Kind" IN ('inventory','connection_open','connection_closed','telemetry_observation','batch_start','batch_status','session','image_request','jpeg','sample_evidence','unassociated_sample_evidence','nh3_image_link','clean_status','operator_event','camera_error','clock_observation','command','command_outcome','schedule_slot'))
        );
        CREATE UNIQUE INDEX "IX_WaterBenchEvidence_StationId_EventId" ON be."WaterBenchEvidence"("StationId","EventId");
        CREATE INDEX "IX_WaterBenchEvidence_StationId_CreatedAt" ON be."WaterBenchEvidence"("StationId","CreatedAt");
        CREATE INDEX "IX_WaterBenchEvidence_Batch" ON be."WaterBenchEvidence"("StationId","ConnectionId","BootId","FirmwareBatchId");
        CREATE INDEX "IX_WaterBenchEvidence_StationId_RequestId" ON be."WaterBenchEvidence"("StationId","RequestId");
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""
        DROP TABLE be."WaterBenchEvidence";
        DROP TABLE be."WaterBenchStations";
        """);
}
