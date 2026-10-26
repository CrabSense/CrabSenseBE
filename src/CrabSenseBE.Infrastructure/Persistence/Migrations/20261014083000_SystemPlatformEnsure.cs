using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CrabSenseBE.Infrastructure.Persistence;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>
/// The generated SystemPlatform migration was marked applied when CredentialHash
/// already existed, so the remaining columns and tables never landed.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261014083000_SystemPlatformEnsure")]
public partial class SystemPlatformEnsure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."Devices"
            ADD COLUMN IF NOT EXISTS "CredentialHash" text NULL,
            ADD COLUMN IF NOT EXISTS "EdgeState" text NOT NULL DEFAULT '',
            ADD COLUMN IF NOT EXISTS "KioskId" uuid NULL,
            ADD COLUMN IF NOT EXISTS "PendingSecret" text NULL;

            ALTER TABLE be."AppUsers"
            ADD COLUMN IF NOT EXISTS "LastLoginIp" text NULL,
            ADD COLUMN IF NOT EXISTS "LastUserAgent" text NULL;

            CREATE TABLE IF NOT EXISTS be."AiManagedServices" (
                "Id" uuid NOT NULL,
                "Name" text NOT NULL,
                "ModelName" text NOT NULL,
                "Version" text NOT NULL,
                "PreviousVersion" text NULL,
                "Status" text NOT NULL,
                "DeviceLabel" text NULL,
                "ConfidenceThreshold" numeric(5,2) NOT NULL,
                "FrameSamplingFps" numeric(6,2) NOT NULL,
                "BatchSize" integer NOT NULL,
                "DetectionClassesJson" text NOT NULL,
                "StartedAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_AiManagedServices" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS be."BackupScheduleSettings" (
                "Id" uuid NOT NULL,
                "Enabled" boolean NOT NULL,
                "Frequency" text NOT NULL,
                "BackupTime" text NOT NULL,
                "RetentionDays" integer NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_BackupScheduleSettings" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS be."CameraRecordingSettings" (
                "Id" uuid NOT NULL,
                "DeviceId" uuid NOT NULL,
                "Mode" text NOT NULL,
                "RetentionDays" integer NOT NULL,
                "VideoQuality" text NULL,
                "Fps" integer NULL,
                "MaxStorageGb" integer NULL,
                "WeeklyScheduleJson" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_CameraRecordingSettings" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_CameraRecordingSettings_Devices_DeviceId"
                    FOREIGN KEY ("DeviceId") REFERENCES be."Devices" ("Id") ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CameraRecordingSettings_DeviceId"
            ON be."CameraRecordingSettings" ("DeviceId");

            CREATE TABLE IF NOT EXISTS be."SecurityPolicySettings" (
                "Id" uuid NOT NULL,
                "SessionDays" integer NOT NULL,
                "FailedLoginAlertThreshold" integer NOT NULL,
                "RequireConfirmRestore" boolean NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_SecurityPolicySettings" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS be."ServiceApiKeys" (
                "Id" uuid NOT NULL,
                "Name" text NOT NULL,
                "Prefix" text NOT NULL,
                "KeyHash" text NOT NULL,
                "LastUsedAt" timestamp with time zone NULL,
                "RevokedAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_ServiceApiKeys" PRIMARY KEY ("Id")
            );

            CREATE INDEX IF NOT EXISTS "IX_ServiceApiKeys_Prefix"
            ON be."ServiceApiKeys" ("Prefix");

            CREATE TABLE IF NOT EXISTS be."SystemDeployments" (
                "Id" uuid NOT NULL,
                "Component" text NOT NULL,
                "Version" text NOT NULL,
                "Environment" text NOT NULL,
                "Status" text NOT NULL,
                "DeployedBy" text NULL,
                "DeployedByUserId" uuid NULL,
                "DeployedAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_SystemDeployments" PRIMARY KEY ("Id")
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS be."SystemDeployments";
            DROP TABLE IF EXISTS be."ServiceApiKeys";
            DROP TABLE IF EXISTS be."SecurityPolicySettings";
            DROP TABLE IF EXISTS be."CameraRecordingSettings";
            DROP TABLE IF EXISTS be."BackupScheduleSettings";
            DROP TABLE IF EXISTS be."AiManagedServices";
            ALTER TABLE be."AppUsers" DROP COLUMN IF EXISTS "LastLoginIp";
            ALTER TABLE be."AppUsers" DROP COLUMN IF EXISTS "LastUserAgent";
            """);
    }
}
