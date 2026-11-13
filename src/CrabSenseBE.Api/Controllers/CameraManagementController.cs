using System.Security.Claims;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize(Roles = AppRoles.Platform)]
[Route("api/cameras")]
public class CameraManagementController(IIotService iot, ISystemPlatformService platform, IUnitOfWork uow) : ControllerBase
{
    private Guid? Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private async Task RequireCamera(Guid id, CancellationToken ct)
    {
        var device = await uow.Devices.GetByIdAsync(id, ct);
        if (device is null || !CameraConfiguration.IsCamera(device))
            throw AppException.NotFound("Camera");
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid? farmingAreaId, Guid? farmingRowId, CancellationToken ct)
    {
        var result = await iot.GetDevicesAsync(farmingAreaId, farmingRowId, ct);
        var cameraIds = (await uow.Devices.GetAllAsync(ct)).Where(CameraConfiguration.IsCamera).Select(x => x.Id).ToHashSet();
        return Ok(ApiResponse<IEnumerable<DeviceDto>>.Ok((result.Data ?? []).Where(x => cameraIds.Contains(x.Id))));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        await RequireCamera(id, ct);
        return Ok(await iot.GetDeviceAsync(id, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateDeviceRequest request, CancellationToken ct)
        => Ok(await iot.CreateDeviceAsync(request with { DeviceType = request.DeviceType?.Contains("cam", StringComparison.OrdinalIgnoreCase) == true ? request.DeviceType : "camera" }, ct, Actor(), Ip()));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateDeviceRequest request, CancellationToken ct)
    {
        await RequireCamera(id, ct);
        if (request.DeviceType is not null && !request.DeviceType.Contains("cam", StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest("Không thể đổi Camera thành Controller.");
        return Ok(await iot.UpdateDeviceAsync(id, request, ct, Actor(), Ip()));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await RequireCamera(id, ct);
        return Ok(await iot.DeleteDeviceAsync(id, ct, Actor(), Ip()));
    }

    [HttpPost("{id:guid}/enable")]
    public async Task<IActionResult> Enable(Guid id, CancellationToken ct)
    {
        await RequireCamera(id, ct);
        return Ok(await iot.UpdateDeviceAsync(id, new UpdateDeviceRequest(null, null, "Offline", null, null, null), ct, Actor(), Ip()));
    }

    [HttpPost("{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        await RequireCamera(id, ct);
        return Ok(await platform.DisableDeviceAsync(id, Actor(), Ip(), ct));
    }

    [HttpGet("{id:guid}/health")]
    public async Task<IActionResult> Health(Guid id, CancellationToken ct)
    {
        await RequireCamera(id, ct);
        return Ok(await platform.GetDeviceDiagnosticsAsync(id, ct));
    }

    [HttpGet("{id:guid}/capabilities")]
    public async Task<IActionResult> Capabilities(Guid id, CancellationToken ct)
    {
        await RequireCamera(id, ct);
        return Ok(ApiResponse<object>.Ok(new { configuration = true, heartbeat = true,
            liveStreaming = false, snapshotCapture = false, recordingRuntime = false, restart = false,
            firmwareUpdate = false, runtimeStatus = "NotIntegrated" }));
    }

    [HttpGet("{id:guid}/recording")]
    public async Task<IActionResult> Recording(Guid id, CancellationToken ct)
    {
        await RequireCamera(id, ct);
        var result = await platform.GetRecordingSettingsAsync(ct);
        return Ok(ApiResponse<CrabSenseBE.Application.DTOs.SystemPlatform.CameraRecordingDto>.Ok(
            result.Data!.SingleOrDefault(x => x.DeviceId == id) ?? throw AppException.NotFound("Camera")));
    }

    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, CancellationToken ct)
    {
        await RequireCamera(id, ct);
        return Ok(await platform.GetDeviceLogsAsync(id, ct));
    }

    [HttpPost("{id:guid}/test-connection")]
    public async Task<IActionResult> Test(Guid id, CancellationToken ct)
    {
        await RequireCamera(id, ct);
        return Ok(await platform.TestDeviceConnectionAsync(id, Actor(), Ip(), ct));
    }

    [HttpGet("/api/system/device-locations")]
    public async Task<IActionResult> Locations(CancellationToken ct)
    {
        var areas = await uow.FarmingAreas.GetAllAsync(ct);
        var rows = await uow.FarmingRows.GetAllAsync(ct);
        return Ok(ApiResponse<object>.Ok(areas.OrderBy(x => x.Code).Select(x => new
        {
            x.Id, x.Code, x.Name, x.IsActive,
            rows = rows.Where(r => r.FarmingAreaId == x.Id).OrderBy(r => r.Code)
                .Select(r => new { r.Id, r.Code, r.Name, r.IsActive })
        })));
    }
}
