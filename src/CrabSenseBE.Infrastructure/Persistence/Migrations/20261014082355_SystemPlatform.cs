using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SystemPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CredentialHash",
                schema: "be",
                table: "Devices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EdgeState",
                schema: "be",
                table: "Devices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "KioskId",
                schema: "be",
                table: "Devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingSecret",
                schema: "be",
                table: "Devices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastLoginIp",
                schema: "be",
                table: "AppUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastUserAgent",
                schema: "be",
                table: "AppUsers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiManagedServices",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ModelName = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    PreviousVersion = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DeviceLabel = table.Column<string>(type: "text", nullable: true),
                    ConfidenceThreshold = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    FrameSamplingFps = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    DetectionClassesJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiManagedServices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupScheduleSettings",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Frequency = table.Column<string>(type: "text", nullable: false),
                    BackupTime = table.Column<string>(type: "text", nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupScheduleSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CameraRecordingSettings",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false),
                    VideoQuality = table.Column<string>(type: "text", nullable: true),
                    Fps = table.Column<int>(type: "integer", nullable: true),
                    MaxStorageGb = table.Column<int>(type: "integer", nullable: true),
                    WeeklyScheduleJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CameraRecordingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CameraRecordingSettings_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "be",
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FarmKiosks",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LanIp = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FarmKiosks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FarmKiosks_FarmingAreas_FarmingAreaId",
                        column: x => x.FarmingAreaId,
                        principalSchema: "be",
                        principalTable: "FarmingAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityPolicySettings",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionDays = table.Column<int>(type: "integer", nullable: false),
                    FailedLoginAlertThreshold = table.Column<int>(type: "integer", nullable: false),
                    RequireConfirmRestore = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityPolicySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceApiKeys",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Prefix = table.Column<string>(type: "text", nullable: false),
                    KeyHash = table.Column<string>(type: "text", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemDeployments",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Component = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DeployedBy = table.Column<string>(type: "text", nullable: true),
                    DeployedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeployedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemDeployments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KioskCredentials",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KioskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretHash = table.Column<string>(type: "text", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KioskCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KioskCredentials_FarmKiosks_KioskId",
                        column: x => x.KioskId,
                        principalSchema: "be",
                        principalTable: "FarmKiosks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KioskProvisioningCodes",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KioskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KioskProvisioningCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KioskProvisioningCodes_FarmKiosks_KioskId",
                        column: x => x.KioskId,
                        principalSchema: "be",
                        principalTable: "FarmKiosks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CameraRecordingSettings_DeviceId",
                schema: "be",
                table: "CameraRecordingSettings",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FarmKiosks_Code",
                schema: "be",
                table: "FarmKiosks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FarmKiosks_FarmingAreaId",
                schema: "be",
                table: "FarmKiosks",
                column: "FarmingAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_KioskCredentials_KioskId",
                schema: "be",
                table: "KioskCredentials",
                column: "KioskId");

            migrationBuilder.CreateIndex(
                name: "IX_KioskProvisioningCodes_Code",
                schema: "be",
                table: "KioskProvisioningCodes",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_KioskProvisioningCodes_KioskId",
                schema: "be",
                table: "KioskProvisioningCodes",
                column: "KioskId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceApiKeys_Prefix",
                schema: "be",
                table: "ServiceApiKeys",
                column: "Prefix");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiManagedServices",
                schema: "be");

            migrationBuilder.DropTable(
                name: "BackupScheduleSettings",
                schema: "be");

            migrationBuilder.DropTable(
                name: "CameraRecordingSettings",
                schema: "be");

            migrationBuilder.DropTable(
                name: "KioskCredentials",
                schema: "be");

            migrationBuilder.DropTable(
                name: "KioskProvisioningCodes",
                schema: "be");

            migrationBuilder.DropTable(
                name: "SecurityPolicySettings",
                schema: "be");

            migrationBuilder.DropTable(
                name: "ServiceApiKeys",
                schema: "be");

            migrationBuilder.DropTable(
                name: "SystemDeployments",
                schema: "be");

            migrationBuilder.DropTable(
                name: "FarmKiosks",
                schema: "be");

            migrationBuilder.DropColumn(
                name: "CredentialHash",
                schema: "be",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "EdgeState",
                schema: "be",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "KioskId",
                schema: "be",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "PendingSecret",
                schema: "be",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastLoginIp",
                schema: "be",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "LastUserAgent",
                schema: "be",
                table: "AppUsers");
        }
    }
}
