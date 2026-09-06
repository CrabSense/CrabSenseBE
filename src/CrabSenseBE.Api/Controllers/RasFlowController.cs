using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/areas")]
[Authorize]
[Tags("08. IoT — RAS flow")]
[Produces("application/json")]
public class AreaRasFlowController : ControllerBase
{
    private readonly IRasFlowService _service;
    public AreaRasFlowController(IRasFlowService service) => _service = service;

    /// <summary>[READ] Sơ đồ tuần hoàn theo khu. Tự tạo WaterSystem + pipeline mặc định nếu chưa có.</summary>
    [HttpGet("{areaId:guid}/ras-flow")]
    public async Task<IActionResult> Get(Guid areaId, CancellationToken ct)
        => Ok(await _service.GetDiagramByAreaAsync(areaId, ct));

    /// <summary>[UPDATE] Đổi thứ tự node + dựng lại WaterFlow tuần tự.</summary>
    [HttpPost("{areaId:guid}/ras-flow/reorder")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Reorder(Guid areaId, [FromBody] ReorderRasFlowRequest req, CancellationToken ct)
        => Ok(await _service.ReorderAsync(areaId, req, ct));

    /// <summary>[CREATE] Thêm RASComponent (Type suy ra từ nodeCode).</summary>
    [HttpPost("{areaId:guid}/ras-flow/nodes")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> AddNode(Guid areaId, [FromBody] CreateRasFlowNodeRequest req, CancellationToken ct)
        => Ok(await _service.AddNodeAsync(areaId, req, ct));

    /// <summary>[DELETE] Xóa node + các WaterFlow liên quan.</summary>
    [HttpDelete("{areaId:guid}/ras-flow/nodes/{nodeId:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> DeleteNode(Guid areaId, Guid nodeId, CancellationToken ct)
        => Ok(await _service.DeleteNodeAsync(areaId, nodeId, ct));

    /// <summary>[UPDATE] Bật/tắt relay trên node (on/off/toggle).</summary>
    [HttpPost("{areaId:guid}/ras-flow/nodes/{nodeId:guid}/command")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Command(
        Guid areaId, Guid nodeId, [FromBody] RasFlowCommandRequest req, CancellationToken ct)
        => Ok(await _service.CommandAsync(areaId, nodeId, req, TryGetUserId(), ct));

    private Guid? TryGetUserId()
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return raw is not null && Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}

[ApiController]
[Route("api/water-systems/{waterSystemId:guid}")]
[Authorize]
[Tags("08. IoT — RAS flow")]
[Produces("application/json")]
public class WaterSystemRasController : ControllerBase
{
    private readonly IRasFlowService _service;
    public WaterSystemRasController(IRasFlowService service) => _service = service;

    [HttpGet("components")]
    public async Task<IActionResult> Components(Guid waterSystemId, CancellationToken ct)
        => Ok(await _service.ListComponentsAsync(waterSystemId, ct));

    [HttpGet("flows")]
    public async Task<IActionResult> Flows(Guid waterSystemId, CancellationToken ct)
        => Ok(await _service.ListFlowsAsync(waterSystemId, ct));
}
