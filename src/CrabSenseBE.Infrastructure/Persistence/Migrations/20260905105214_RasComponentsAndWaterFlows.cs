using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>Additive: RASComponent + WaterFlow + WaterSystem status + Sensor.RasComponentId.</summary>
public partial class RasComponentsAndWaterFlows : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."WaterSystems"
            ADD COLUMN IF NOT EXISTS "Status" character varying(32) NOT NULL DEFAULT 'active',
            ADD COLUMN IF NOT EXISTS "FlowStatus" character varying(32) NULL,
            ADD COLUMN IF NOT EXISTS "Description" text NULL;

            ALTER TABLE be."Sensors"
            ADD COLUMN IF NOT EXISTS "RasComponentId" uuid NULL;

            CREATE TABLE IF NOT EXISTS be."RasComponents" (
                "Id" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "WaterSystemId" uuid NOT NULL,
                "Code" character varying(64) NOT NULL,
                "Name" character varying(128) NOT NULL,
                "Type" character varying(32) NOT NULL,
                "Status" character varying(32) NOT NULL,
                "Position" integer NOT NULL,
                "Capacity" numeric(12,2) NULL,
                "Description" text NULL,
                "NodeType" text NULL,
                "IconKey" text NULL,
                "RelayDeviceId" uuid NULL,
                "RelayChannel" text NULL,
                "ParamDefaultsJson" text NULL,
                "HasRelay" boolean NOT NULL,
                "IsOn" boolean NOT NULL,
                "ControlMode" text NULL,
                CONSTRAINT "PK_RasComponents" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_RasComponents_WaterSystems_WaterSystemId"
                    FOREIGN KEY ("WaterSystemId") REFERENCES be."WaterSystems" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_RasComponents_Devices_RelayDeviceId"
                    FOREIGN KEY ("RelayDeviceId") REFERENCES be."Devices" ("Id") ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS "IX_RasComponents_WaterSystemId_Position"
                ON be."RasComponents" ("WaterSystemId", "Position");
            CREATE INDEX IF NOT EXISTS "IX_RasComponents_RelayDeviceId"
                ON be."RasComponents" ("RelayDeviceId");

            CREATE TABLE IF NOT EXISTS be."WaterFlows" (
                "Id" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "WaterSystemId" uuid NOT NULL,
                "FromComponentId" uuid NOT NULL,
                "ToComponentId" uuid NOT NULL,
                "FlowRate" numeric(12,2) NULL,
                "Status" character varying(32) NOT NULL,
                "SortOrder" integer NOT NULL,
                CONSTRAINT "PK_WaterFlows" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_WaterFlows_WaterSystems_WaterSystemId"
                    FOREIGN KEY ("WaterSystemId") REFERENCES be."WaterSystems" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_WaterFlows_RasComponents_FromComponentId"
                    FOREIGN KEY ("FromComponentId") REFERENCES be."RasComponents" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_WaterFlows_RasComponents_ToComponentId"
                    FOREIGN KEY ("ToComponentId") REFERENCES be."RasComponents" ("Id") ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS "IX_WaterFlows_WaterSystemId" ON be."WaterFlows" ("WaterSystemId");
            CREATE INDEX IF NOT EXISTS "IX_WaterFlows_FromComponentId" ON be."WaterFlows" ("FromComponentId");
            CREATE INDEX IF NOT EXISTS "IX_WaterFlows_ToComponentId" ON be."WaterFlows" ("ToComponentId");

            CREATE INDEX IF NOT EXISTS "IX_Sensors_RasComponentId" ON be."Sensors" ("RasComponentId");
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."Sensors" DROP CONSTRAINT IF EXISTS "FK_Sensors_RasComponents_RasComponentId";
            DROP INDEX IF EXISTS be."IX_Sensors_RasComponentId";
            ALTER TABLE be."Sensors" DROP COLUMN IF EXISTS "RasComponentId";

            DROP TABLE IF EXISTS be."WaterFlows";
            DROP TABLE IF EXISTS be."RasComponents";

            ALTER TABLE be."WaterSystems" DROP COLUMN IF EXISTS "Status";
            ALTER TABLE be."WaterSystems" DROP COLUMN IF EXISTS "FlowStatus";
            ALTER TABLE be."WaterSystems" DROP COLUMN IF EXISTS "Description";
            """);
    }
}
