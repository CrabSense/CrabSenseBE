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

    /// <summary>[CREATE] Bắt đầu quy trình: lấy mẫu → thuốc thử → camera → AI.</summary>
    [HttpPost("{areaId:guid}/water-analysis/start")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Start(Guid areaId, CancellationToken ct)
        => Ok(await _service.StartAsync(areaId, ct));
}
