using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CrabSenseBE.Application.DTOs.SystemPlatform;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize(Roles = AppRoles.Platform)]
public class SystemPlatformController : ControllerBase
{
    private readonly ISystemPlatformService _platform;

    public SystemPlatformController(ISystemPlatformService platform) => _platform = platform;

    [HttpGet("api/system/health")]
    public async Task<IActionResult> Health(CancellationToken ct) => Ok(await _platform.GetHealthAsync(ct));

    [HttpGet("api/system/services")]
    public async Task<IActionResult> Services(CancellationToken ct) => Ok(await _platform.GetServicesAsync(ct));

    [HttpGet("api/system/metrics")]
    public async Task<IActionResult> Metrics(CancellationToken ct) => Ok(await _platform.GetMetricsAsync(ct));

    [HttpGet("api/system/versions")]
    public async Task<IActionResult> Versions(CancellationToken ct) => Ok(await _platform.GetVersionsAsync(ct));

    [HttpGet("api/system/deployments")]
    public async Task<IActionResult> Deployments(CancellationToken ct) => Ok(await _platform.GetDeploymentsAsync(ct));

    [HttpPost("api/system/deploy")]
    public async Task<IActionResult> Deploy([FromBody] DeployRequest request, CancellationToken ct)
        => Ok(await _platform.DeployAsync(request, UserId(), Actor(), ct));

    [HttpPost("api/system/rollback")]
    public async Task<IActionResult> Rollback([FromBody] RollbackRequest request, CancellationToken ct)
        => Ok(await _platform.RollbackAsync(request, UserId(), Actor(), ct));

    [HttpPost("api/system/restart")]
    public async Task<IActionResult> Restart([FromQuery] string? component, CancellationToken ct)
        => Ok(await _platform.RequestRestartAsync(component ?? "Backend API", UserId(), Actor(), ct));

    [HttpGet("api/system/logs")]
    public async Task<IActionResult> Logs(
        [FromQuery] string? source,
        [FromQuery] string? severity,
        [FromQuery] string? search,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
        => Ok(await _platform.GetLogsAsync(source, severity, search, from, to, ct));

    [HttpPost("api/devices/{id:guid}/restart")]
    public async Task<IActionResult> RestartDevice(Guid id, CancellationToken ct)
        => Ok(await _platform.RestartDeviceAsync(id, UserId(), Ip(), ct));

    [HttpPost("api/devices/{id:guid}/firmware")]
    public async Task<IActionResult> Firmware(Guid id, [FromBody] UpdateFirmwareRequest request, CancellationToken ct)
        => Ok(await _platform.UpdateFirmwareAsync(id, request, UserId(), Ip(), ct));

    [HttpPost("api/devices/{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
        => Ok(await _platform.DisableDeviceAsync(id, UserId(), Ip(), ct));

    [HttpPost("api/devices/{id:guid}/test-connection")]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken ct)
        => Ok(await _platform.TestDeviceConnectionAsync(id, UserId(), Ip(), ct));

    [HttpGet("api/devices/{id:guid}/diagnostics")]
    public async Task<IActionResult> Diagnostics(Guid id, CancellationToken ct)
        => Ok(await _platform.GetDeviceDiagnosticsAsync(id, ct));

    [HttpGet("api/devices/{id:guid}/logs")]
    public async Task<IActionResult> DeviceLogs(Guid id, CancellationToken ct)
        => Ok(await _platform.GetDeviceLogsAsync(id, ct));

    [HttpGet("api/cameras/recording-schedules")]
    public async Task<IActionResult> Schedules(CancellationToken ct)
        => Ok(await _platform.GetRecordingSettingsAsync(ct));

    [HttpPut("api/cameras/{id:guid}/recording")]
    public async Task<IActionResult> SaveRecording(Guid id, [FromBody] UpsertCameraRecordingRequest request, CancellationToken ct)
        => Ok(await _platform.UpsertRecordingAsync(id, request, UserId(), Ip(), ct));

    [HttpGet("api/cameras/recordings")]
    public async Task<IActionResult> Recordings([FromQuery] Guid? deviceId, CancellationToken ct)
        => Ok(await _platform.GetRecordingHistoryAsync(deviceId, ct));

    [HttpGet("api/ai/services")]
    public async Task<IActionResult> AiServices(CancellationToken ct) => Ok(await _platform.GetAiServicesAsync(ct));

    [HttpPost("api/ai/services")]
    public async Task<IActionResult> CreateAi([FromBody] UpsertAiServiceRequest request, CancellationToken ct)
        => Ok(await _platform.CreateAiServiceAsync(request, UserId(), Ip(), ct));

    [HttpPost("api/ai/services/{id:guid}/restart")]
    public async Task<IActionResult> RestartAi(Guid id, CancellationToken ct)
        => Ok(await _platform.RestartAiServiceAsync(id, UserId(), Ip(), ct));

    [HttpPost("api/ai/services/{id:guid}/stop")]
    public async Task<IActionResult> StopAi(Guid id, CancellationToken ct)
        => Ok(await _platform.StopAiServiceAsync(id, UserId(), Ip(), ct));

    [HttpPost("api/ai/services/{id:guid}/deploy")]
    public async Task<IActionResult> DeployAi(Guid id, [FromBody] DeployModelRequest request, CancellationToken ct)
        => Ok(await _platform.DeployAiModelAsync(id, request, UserId(), Ip(), ct));

    [HttpPost("api/ai/services/{id:guid}/rollback")]
    public async Task<IActionResult> RollbackAi(Guid id, CancellationToken ct)
        => Ok(await _platform.RollbackAiModelAsync(id, UserId(), Ip(), ct));

    [HttpGet("api/storage/capacity")]
    public async Task<IActionResult> Capacity(CancellationToken ct) => Ok(await _platform.GetStorageCapacityAsync(ct));

    [HttpGet("api/storage/usage")]
    public async Task<IActionResult> Usage(CancellationToken ct) => Ok(await _platform.GetStorageUsageAsync(ct));

    [HttpGet("api/storage/usage-history")]
    public async Task<IActionResult> UsageHistory([FromQuery] int days = 30, CancellationToken ct = default)
        => Ok(await _platform.GetStorageHistoryAsync(days, ct));

    [HttpGet("api/admin/db/backup-schedule")]
    public async Task<IActionResult> BackupSchedule(CancellationToken ct)
        => Ok(await _platform.GetBackupScheduleAsync(ct));

    [HttpPut("api/admin/db/backup-schedule")]
    public async Task<IActionResult> SaveBackupSchedule([FromBody] BackupScheduleDto request, CancellationToken ct)
        => Ok(await _platform.UpdateBackupScheduleAsync(request, UserId(), Ip(), ct));

    [HttpGet("api/security/overview")]
    public async Task<IActionResult> Security(CancellationToken ct) => Ok(await _platform.GetSecurityOverviewAsync(ct));

    [HttpGet("api/security/sessions")]
    public async Task<IActionResult> Sessions(CancellationToken ct) => Ok(await _platform.GetSessionsAsync(ct));

    [HttpDelete("api/security/sessions/{userId:guid}")]
    public async Task<IActionResult> Terminate(Guid userId, CancellationToken ct)
        => Ok(await _platform.TerminateSessionAsync(userId, UserId(), Ip(), ct));

    [HttpGet("api/security/api-keys")]
    public async Task<IActionResult> Keys(CancellationToken ct) => Ok(await _platform.GetApiKeysAsync(ct));

    [HttpPost("api/security/api-keys")]
    public async Task<IActionResult> CreateKey([FromBody] CreateApiKeyRequest request, CancellationToken ct)
        => Ok(await _platform.CreateApiKeyAsync(request, UserId(), Ip(), ct));

    [HttpPost("api/security/api-keys/{id:guid}/rotate")]
    public async Task<IActionResult> Rotate(Guid id, CancellationToken ct)
        => Ok(await _platform.RotateApiKeyAsync(id, UserId(), Ip(), ct));

    [HttpDelete("api/security/api-keys/{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
        => Ok(await _platform.RevokeApiKeyAsync(id, UserId(), Ip(), ct));

    [HttpGet("api/security/policies")]
    public async Task<IActionResult> Policy(CancellationToken ct) => Ok(await _platform.GetPolicyAsync(ct));

    [HttpPut("api/security/policies")]
    public async Task<IActionResult> SavePolicy([FromBody] SecurityPolicyDto request, CancellationToken ct)
        => Ok(await _platform.UpdatePolicyAsync(request, UserId(), Ip(), ct));

    [HttpGet("api/security/audit-logs")]
    public async Task<IActionResult> Audit(CancellationToken ct) => Ok(await _platform.GetAuditLogsAsync(ct));

    private Guid? UserId()
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private string? Actor()
        => User.Identity?.Name
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue("unique_name");

    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
