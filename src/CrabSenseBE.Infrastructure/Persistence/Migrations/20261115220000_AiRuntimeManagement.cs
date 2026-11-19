using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261115220000_AiRuntimeManagement")]
public sealed class AiRuntimeManagement : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE be."AiManagedServices" ADD COLUMN "IsEnabled" boolean NOT NULL DEFAULT true,
          ADD COLUMN "LastHeartbeatAt" timestamptz NULL, ADD COLUMN "ReportedState" text NULL,
          ADD COLUMN "ReportedVersion" text NULL, ADD COLUMN "CommandId" uuid NULL,
          ADD COLUMN "RequestedVersion" text NULL, ADD COLUMN "CommandRequestedAt" timestamptz NULL,
          ADD COLUMN "CommandError" text NULL;
        ALTER TABLE be."AiDetections" ADD COLUMN "AiServiceId" uuid NULL, ADD COLUMN "InferenceJobId" uuid NULL, ADD COLUMN "FarmingAreaId" uuid NULL;
        CREATE INDEX "IX_AiDetections_AiServiceId" ON be."AiDetections"("AiServiceId");
        CREATE UNIQUE INDEX "IX_AiDetections_InferenceJobId" ON be."AiDetections"("InferenceJobId");
        CREATE TABLE be."AiModelVersions" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "AiServiceId" uuid NOT NULL REFERENCES be."AiManagedServices"("Id") ON DELETE RESTRICT,
          "Version" text NOT NULL, "ArtifactName" text NOT NULL, "Sha256" text NOT NULL);
        CREATE UNIQUE INDEX "IX_AiModelVersions_AiServiceId_Version" ON be."AiModelVersions"("AiServiceId","Version");
        CREATE TABLE be."AiCameraAssignments" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "AiServiceId" uuid NOT NULL REFERENCES be."AiManagedServices"("Id") ON DELETE RESTRICT,
          "DeviceId" uuid NOT NULL REFERENCES be."Devices"("Id") ON DELETE CASCADE);
        CREATE UNIQUE INDEX "IX_AiCameraAssignments_AiServiceId_DeviceId" ON be."AiCameraAssignments"("AiServiceId","DeviceId");
        CREATE INDEX "IX_AiCameraAssignments_DeviceId" ON be."AiCameraAssignments"("DeviceId");
        CREATE TABLE be."AiInferenceJobs" (
          "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
          "AiServiceId" uuid NOT NULL REFERENCES be."AiManagedServices"("Id") ON DELETE RESTRICT,
          "ModelVersionId" uuid NOT NULL REFERENCES be."AiModelVersions"("Id") ON DELETE RESTRICT,
          "MediaId" uuid NOT NULL, "DeviceId" uuid NOT NULL, "BoxId" uuid NOT NULL, "CrabId" uuid NULL,
          "FarmingAreaId" uuid NOT NULL, "ModelVersion" text NOT NULL,
          "ConfidenceThreshold" numeric NOT NULL, "FrameSamplingFps" numeric NOT NULL,
          "DetectionClassesJson" text NOT NULL, "Status" text NOT NULL,
          "Attempts" integer NOT NULL, "LeaseId" uuid NULL, "LeaseUntil" timestamptz NULL,
          "CompletedAt" timestamptz NULL, "Error" text NULL, "DurationMs" bigint NULL);
        CREATE UNIQUE INDEX "IX_AiInferenceJobs_AiServiceId_MediaId_ModelVersionId" ON be."AiInferenceJobs"("AiServiceId","MediaId","ModelVersionId");
        CREATE INDEX "IX_AiInferenceJobs_ModelVersionId" ON be."AiInferenceJobs"("ModelVersionId");
        CREATE INDEX "IX_AiInferenceJobs_AiServiceId_Status_CreatedAt" ON be."AiInferenceJobs"("AiServiceId","Status","CreatedAt");
        """);
    protected override void Down(MigrationBuilder m) => m.Sql("""
        DROP TABLE be."AiInferenceJobs", be."AiCameraAssignments", be."AiModelVersions";
        DROP INDEX be."IX_AiDetections_AiServiceId", be."IX_AiDetections_InferenceJobId";
        ALTER TABLE be."AiDetections" DROP COLUMN "AiServiceId", DROP COLUMN "InferenceJobId", DROP COLUMN "FarmingAreaId";
        ALTER TABLE be."AiManagedServices" DROP COLUMN "IsEnabled", DROP COLUMN "LastHeartbeatAt",
          DROP COLUMN "ReportedState", DROP COLUMN "ReportedVersion", DROP COLUMN "CommandId",
          DROP COLUMN "RequestedVersion", DROP COLUMN "CommandRequestedAt", DROP COLUMN "CommandError";
        """);
}
