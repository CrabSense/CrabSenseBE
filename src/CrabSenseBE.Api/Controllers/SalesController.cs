using CrabSenseBE.Application.DTOs.Sales;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize]
[Tags("36. Sales (Mobile)")]
[Produces("application/json")]
public class SalesController : ControllerBase
{
    private readonly ISalesService _service;
    public SalesController(ISalesService service) => _service = service;

    /// <summary>[CREATE] Quick sale</summary>
    [HttpPost("create")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create([FromBody] CreateSaleRequest req, CancellationToken ct)
        => Ok(await _service.CreateAsync(req, ct));

    /// <summary>[CREATE] Alias POST /api/sales</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> CreateRoot([FromBody] CreateSaleRequest req, CancellationToken ct)
        => Ok(await _service.CreateAsync(req, ct));

    /// <summary>[READ] Sales history</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] Guid? farmId = null,
        [FromQuery] string? buyerName = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string? paymentMethod = null,
        [FromQuery] string? paymentStatus = null,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
        => Ok(await _service.GetHistoryAsync(
            farmId, buyerName, startDate, endDate, paymentMethod, paymentStatus, page, limit, ct));

    /// <summary>[READ] Sales summary</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] Guid? farmId = null,
        CancellationToken ct = default)
        => Ok(await _service.GetSummaryAsync(startDate, endDate, farmId, ct));

    /// <summary>[READ] Available harvested inventory (kg)</summary>
    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory([FromQuery] Guid? farmId = null, CancellationToken ct = default)
        => Ok(await _service.GetAvailableInventoryAsync(farmId, ct));

    /// <summary>[READ] Sale by id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));
}
