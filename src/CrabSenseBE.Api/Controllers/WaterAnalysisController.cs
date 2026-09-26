using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
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
    public WaterAnalysisController(IWaterAnalysisService service) => _service = service;

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
}
