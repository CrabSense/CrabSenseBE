using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/areas")]
[Authorize]
[Tags("08. IoT — Water analysis")]
[Produces("application/json")]
public class WaterAnalysisController : ControllerBase
{
    private readonly IWaterAnalysisService _service;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _uow;
    public WaterAnalysisController(
        IWaterAnalysisService service, IConfiguration configuration, IUnitOfWork uow)
    {
        _service = service;
        _configuration = configuration;
        _uow = uow;
    }

    /// <summary>[READ] Kết quả phân tích hóa học mới nhất + trạng thái trạm + lần đang chạy.</summary>
    [HttpGet("{areaId:guid}/water-analysis")]
    public async Task<IActionResult> Get(Guid areaId, CancellationToken ct)
        => Ok(await _service.GetSnapshotAsync(areaId, ct));

    /// <summary>[READ] Hồ sơ một lần phân tích (drawer chi tiết).</summary>
    [HttpGet("{areaId:guid}/water-analysis/{runId:guid}")]
    public async Task<IActionResult> GetDetail(Guid areaId, Guid runId, CancellationToken ct)
        => Ok(await _service.GetDetailAsync(areaId, runId, ct));

    /// <summary>[CREATE] Bắt đầu quy trình: lấy mẫu → thuốc thử → camera → AI. Không chạy nếu thiếu điều kiện.</summary>
    [HttpPost("{areaId:guid}/water-analysis/start")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Start(
        Guid areaId, [FromBody] StartWaterAnalysisRequest? req, CancellationToken ct)
        => Ok(await _service.StartAsync(areaId, req, ct));

    /// <summary>[UPDATE] Dừng lần đang chạy và chuyển sang xả/làm sạch an toàn.</summary>
    [HttpPost("{areaId:guid}/water-analysis/stop")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Stop(Guid areaId, CancellationToken ct)
        => Ok(await _service.StopAsync(areaId, ct));

    /// <summary>[UPDATE] Cập nhật thông tin mẫu (không đổi phần cứng).</summary>
    [HttpPut("{areaId:guid}/water-analysis/sample")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> UpdateSample(
        Guid areaId, [FromBody] UpdateWaterAnalysisSampleRequest req, CancellationToken ct)
        => Ok(await _service.UpdateSampleAsync(areaId, req, ct));

    /// <summary>Controller đã xác thực ACK bước hardware; timer không thể gọi endpoint này.</summary>
    [HttpPost("water-analysis/runs/{runId:guid}/events")]
    [AllowAnonymous]
    public async Task<IActionResult> RecordHardwareEvent(
        Guid runId, [FromBody] WaterAnalysisHardwareEventRequest req, CancellationToken ct)
    {
        var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(
            Request, _configuration, _uow, req.DeviceCode, ct);
        if (denied is not null) return denied;
        return Ok(await _service.RecordHardwareEventAsync(
            runId, req with { DeviceCode = identity!.DeviceCode }, ct));
    }
}
