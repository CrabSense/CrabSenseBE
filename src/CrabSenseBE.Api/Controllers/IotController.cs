using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/iot")]
[Produces("application/json")]
public class IotController : ControllerBase
{
    private readonly IIotService _service;
    public IotController(IIotService service) => _service = service;

    /// <summary>Ingest single sensor reading (from device or Kiosk hot path)</summary>
    [HttpPost("sensor-data")]
    [AllowAnonymous] // device API key auth handled separately or via ApiKey middleware
    public async Task<IActionResult> IngestSingle([FromBody] SensorDataRequest req, CancellationToken ct)
        => Ok(await _service.IngestSensorDataAsync(req, ct));

    /// <summary>Batch ingest sensor readings</summary>
    [HttpPost("sensor-data/batch")]
    [AllowAnonymous]
    public async Task<IActionResult> IngestBatch([FromBody] SensorDataBatchRequest req, CancellationToken ct)
        => Ok(await _service.IngestSensorDataBatchAsync(req, ct));

    /// <summary>Get sensor history with optional time range + pagination</summary>
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
}

[ApiController]
[Route("api/sensors")]
[Authorize]
[Produces("application/json")]
public class SensorsController : ControllerBase
{
    private readonly IIotService _service;
    public SensorsController(IIotService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetSensorsAsync(ct));

    [HttpPost]
    [Authorize(Roles = "Admin,SystemAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateSensorRequest req, CancellationToken ct)
        => Ok(await _service.CreateSensorAsync(req, ct));
}

[ApiController]
[Route("api/devices")]
[Authorize]
[Produces("application/json")]
public class DevicesController : ControllerBase
{
    private readonly IIotService _service;
    public DevicesController(IIotService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetDevicesAsync(ct));

    [HttpPut("{id:guid}/status")]
    [AllowAnonymous] // called by device
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateDeviceStatusRequest req, CancellationToken ct)
        => Ok(await _service.UpdateDeviceStatusAsync(id, req, ct));
}

[ApiController]
[Route("api/sync")]
[Produces("application/json")]
public class SyncController : ControllerBase
{
    private readonly IIotService _service;
    public SyncController(IIotService service) => _service = service;

    /// <summary>POST /api/sync/hdf5 — Edge Kiosk uploads sealed HDF5 file</summary>
    [HttpPost("hdf5")]
    [AllowAnonymous] // authenticated by ApiKey in header (x-api-key)
    public async Task<IActionResult> UploadHdf5([FromForm] Hdf5UploadRequest req, IFormFile file, CancellationToken ct)
    {
        using var stream = file.OpenReadStream();
        return Ok(await _service.ProcessHdf5UploadAsync(req, stream, ct));
    }
}
