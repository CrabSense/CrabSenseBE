using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
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
    public IotController(IIotService service) => _service = service;

    /// <summary>[CREATE] ESP32 / edge: ingest single reading (HTTP)</summary>
    [HttpPost("sensor-data")]
    [AllowAnonymous]
    public async Task<IActionResult> IngestSingle([FromBody] SensorDataRequest req, CancellationToken ct)
        => Ok(await _service.IngestSensorDataAsync(req, ct));

    /// <summary>[CREATE] ESP32 / edge: ingest batch</summary>
    [HttpPost("sensor-data/batch")]
    [AllowAnonymous]
    public async Task<IActionResult> IngestBatch([FromBody] SensorDataBatchRequest req, CancellationToken ct)
        => Ok(await _service.IngestSensorDataBatchAsync(req, ct));

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
}

[ApiController]
[Route("api/sensors")]
[Authorize]
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
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create([FromBody] CreateSensorRequest req, CancellationToken ct)
        => Ok(await _service.CreateSensorAsync(req, ct));

    /// <summary>[UPDATE] Update sensor</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSensorRequest req, CancellationToken ct)
        => Ok(await _service.UpdateSensorAsync(id, req, ct));

    /// <summary>[DELETE] Delete sensor (blocked if has measurements)</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteSensorAsync(id, ct));
}

[ApiController]
[Route("api/devices")]
[Route("api/controllers")]
[Authorize]
[Tags("09. IoT — Controllers (ESP32)")]
[Produces("application/json")]
public class DevicesController : ControllerBase
{
    private readonly IIotService _service;
    public DevicesController(IIotService service) => _service = service;

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

    /// <summary>[CREATE] Register ESP32 gateway</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create([FromBody] CreateDeviceRequest req, CancellationToken ct)
        => Ok(await _service.CreateDeviceAsync(req, ct));

    /// <summary>[UPDATE] Update device metadata</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeviceRequest req, CancellationToken ct)
        => Ok(await _service.UpdateDeviceAsync(id, req, ct));

    /// <summary>[DELETE] Delete device (no linked sensors)</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteDeviceAsync(id, ct));

    /// <summary>[UPDATE] Heartbeat / status from edge</summary>
    [HttpPut("{id:guid}/status")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateDeviceStatusRequest req, CancellationToken ct)
        => Ok(await _service.UpdateDeviceStatusAsync(id, req, ct));
}

[ApiController]
[Route("api/water-systems")]
[Authorize]
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
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create([FromBody] CreateWaterSystemRequest req, CancellationToken ct)
        => Ok(await _service.CreateWaterSystemAsync(req, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWaterSystemRequest req, CancellationToken ct)
        => Ok(await _service.UpdateWaterSystemAsync(id, req, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
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
    public SyncController(IIotService service) => _service = service;

    /// <summary>[CREATE] Kiosk HDF5 upload</summary>
    [HttpPost("hdf5")]
    [AllowAnonymous]
    public async Task<IActionResult> UploadHdf5([FromForm] Hdf5UploadRequest req, IFormFile file, CancellationToken ct)
    {
        using var stream = file.OpenReadStream();
        return Ok(await _service.ProcessHdf5UploadAsync(req, stream, ct));
    }
}
