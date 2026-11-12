using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/iot")]
[Tags("08. IoT — Sensor data")]
[Produces("application/json")]
public class IotController : ControllerBase
{
    private readonly IIotService _service;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _uow;
    public IotController(IIotService service, IConfiguration configuration, IUnitOfWork uow)
    {
        _service = service;
        _configuration = configuration;
        _uow = uow;
    }

    /// <summary>[CREATE] ESP32 / edge: ingest single reading (HTTP) — yêu cầu X-Edge-Key</summary>
    [HttpPost("sensor-data")]
    [AllowAnonymous]
    public async Task<IActionResult> IngestSingle([FromBody] SensorDataRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceCode))
            return Unauthorized(new
            {
                success = false,
                message = "DeviceCode và credential riêng là bắt buộc."
            });
        var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(
            Request, _configuration, _uow, req.DeviceCode, ct);
        if (denied is not null) return denied;
        return Ok(await _service.IngestSensorDataAsync(
            req, identity!.DeviceId, identity.DeviceCode, ct));
    }

    /// <summary>[CREATE] ESP32 / edge: ingest batch — yêu cầu X-Edge-Key</summary>
    [HttpPost("sensor-data/batch")]
    [AllowAnonymous]
    public async Task<IActionResult> IngestBatch([FromBody] SensorDataBatchRequest req, CancellationToken ct)
    {
        var measurements = (req.Measurements ?? []).ToList();
        if (measurements.Count == 0
            || measurements.Any(item => string.IsNullOrWhiteSpace(item.DeviceCode)))
            return Unauthorized(new
            {
                success = false,
                message = "Mọi measurement phải có DeviceCode của thiết bị đã xác thực."
            });
        var deviceCodes = measurements
            .Select(item => item.DeviceCode.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (deviceCodes.Length != 1)
            return BadRequest(new { success = false, message = "Một batch chỉ được chứa dữ liệu từ một Device." });
        var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(
            Request, _configuration, _uow, deviceCodes[0], ct);
        if (denied is not null) return denied;
        return Ok(await _service.IngestSensorDataBatchAsync(
            req, identity!.DeviceId, identity.DeviceCode, ct));
    }

    /// <summary>Controller đã xác thực báo trạng thái relay quan sát thực tế.</summary>
    [HttpPost("relay-state")]
    [AllowAnonymous]
    public async Task<IActionResult> ReportRelay([FromBody] RelayStateRequest req, CancellationToken ct)
    {
        var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(
            Request, _configuration, _uow, req.DeviceCode, ct);
        if (denied is not null) return denied;
        await _service.ReportRelayAsync(
            identity!.DeviceCode, req.Channel, req.On, null, ct);
        return Ok(ApiResponse.Ok("Relay state recorded."));
    }

    /// <summary>[READ] Sensor history (charts)</summary>
    [HttpGet("sensor-data/{sensorId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetHistory(
        Guid sensorId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
        => Ok(await _service.GetSensorDataAsync(sensorId, from, to, page, pageSize, ct));

    /// <summary>[READ] Latest reading for one sensor</summary>
    [HttpGet("sensor-data/{sensorId:guid}/latest")]
    [Authorize]
    public async Task<IActionResult> GetLatest(Guid sensorId, CancellationToken ct)
        => Ok(await _service.GetLatestSensorDataAsync(sensorId, ct));

    /// <summary>[READ] Kiosk live snapshot — optional deviceId / farmingAreaId</summary>
    [HttpGet("live")]
    [Authorize]
    public async Task<IActionResult> Live(
        [FromQuery] Guid? deviceId = null,
        [FromQuery] Guid? farmingAreaId = null,
        CancellationToken ct = default)
        => Ok(await _service.GetLiveSnapshotAsync(deviceId, farmingAreaId, ct));

    /// <summary>[READ] Aggregated water-quality history for charts (period=24h|7d|30d)</summary>
    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> History(
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] string period = "24h",
        CancellationToken ct = default)
        => Ok(await _service.GetWaterQualityHistoryAsync(farmingAreaId, period, ct));
}

[ApiController]
[Route("api/sensors")]
[Authorize(Roles = AppRoles.Platform)]
[Tags("08. IoT — Sensors")]
[Produces("application/json")]
public class SensorsController : ControllerBase
{
    private readonly IIotService _service;
    public SensorsController(IIotService service) => _service = service;

    /// <summary>[READ] List sensors (optional deviceId)</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? deviceId = null,
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] Guid? farmingRowId = null,
        CancellationToken ct = default)
        => Ok(await _service.GetSensorsAsync(deviceId, farmingAreaId, farmingRowId, ct));

    /// <summary>[READ] Sensor by id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetSensorAsync(id, ct));

    /// <summary>[CREATE] Register sensor under optional ESP32 device</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSensorRequest req, CancellationToken ct)
        => Ok(await _service.CreateSensorAsync(req, ct));

    /// <summary>[UPDATE] Update sensor</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSensorRequest req, CancellationToken ct)
        => Ok(await _service.UpdateSensorAsync(id, req, ct));

    /// <summary>[DELETE] Delete sensor (blocked if has measurements)</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteSensorAsync(id, ct));
}

[ApiController]
[Route("api/devices")]
[Route("api/controllers")]
[Authorize(Roles = AppRoles.FarmPortal + "," + AppRoles.Platform)]
[Tags("09. IoT — Controllers (ESP32)")]
[Produces("application/json")]
public class DevicesController : ControllerBase
{
    private readonly IIotService _service;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _uow;
    public DevicesController(
        IIotService service,
        IConfiguration configuration,
        IUnitOfWork uow)
    {
        _service = service;
        _configuration = configuration;
        _uow = uow;
    }

    /// <summary>[READ] List ESP32 / gateways — optional farmingAreaId</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] Guid? farmingRowId = null,
        CancellationToken ct = default)
        => Ok(await _service.GetDevicesAsync(farmingAreaId, farmingRowId, ct));

    /// <summary>[READ] Device by id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetDeviceAsync(id, ct));

    /// <summary>[CREATE] Register ESP32 gateway — System Administrator.</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.Platform)]
    public async Task<IActionResult> Create([FromBody] CreateDeviceRequest req, CancellationToken ct)
        => Ok(await _service.CreateDeviceAsync(req, ct, UserId(), Ip()));

    /// <summary>[UPDATE] Update device metadata — System Administrator.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.Platform)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeviceRequest req, CancellationToken ct)
        => Ok(await _service.UpdateDeviceAsync(id, req, ct, UserId(), Ip()));

    /// <summary>[DELETE] Delete device (no linked sensors) — System Administrator.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.Platform)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteDeviceAsync(id, ct, UserId(), Ip()));

    /// <summary>[UPDATE] Heartbeat / status from the authenticated device.</summary>
    [HttpPut("{id:guid}/status")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateDeviceStatusRequest req, CancellationToken ct)
    {
        var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(
            Request, _configuration, _uow, id, ct);
        if (denied is not null) return denied;
        return Ok(await _service.UpdateDeviceStatusAsync(
            identity!.DeviceId, req, ct));
    }

    private Guid? UserId()
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

[ApiController]
[Route("api/water-systems")]
[Authorize(Roles = AppRoles.FarmPortal + "," + AppRoles.Platform)]
[Tags("08. IoT — Water systems")]
[Produces("application/json")]
public class WaterSystemsController : ControllerBase
{
    private readonly IIotService _service;
    public WaterSystemsController(IIotService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetWaterSystemsAsync(ct));

    [HttpPost]
    [Authorize(Roles = AppRoles.FarmManage)]
    public async Task<IActionResult> Create([FromBody] CreateWaterSystemRequest req, CancellationToken ct)
        => Ok(await _service.CreateWaterSystemAsync(req, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWaterSystemRequest req, CancellationToken ct)
        => Ok(await _service.UpdateWaterSystemAsync(id, req, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteWaterSystemAsync(id, ct));
}

[ApiController]
[Route("api/sync")]
[Tags("09. IoT — Edge sync")]
[Produces("application/json")]
public class SyncController : ControllerBase
{
    private readonly IIotService _service;
    private readonly IUnitOfWork _uow;
    public SyncController(IIotService service, IUnitOfWork uow)
    {
        _service = service;
        _uow = uow;
    }

    /// <summary>[CREATE] Kiosk HDF5 upload — requires the device credential.</summary>
    [HttpPost("hdf5")]
    [AllowAnonymous]
    public async Task<IActionResult> UploadHdf5(
        [FromForm] Hdf5UploadRequest req,
        IFormFile file,
        CancellationToken ct,
        [FromServices] IConfiguration configuration)
    {
        var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(
            Request, configuration, _uow, req.DeviceCode, ct);
        if (denied is not null) return denied;
        using var stream = file.OpenReadStream();
        return Ok(await _service.ProcessHdf5UploadAsync(
            req, stream, identity!.DeviceId, identity.DeviceCode, ct));
    }
}
