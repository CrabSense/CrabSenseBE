using CrabSenseBE.Application.DTOs.Condition;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Crab condition (Kn) APIs — port of crab_condition Python module for Mobile / FE.
/// </summary>
[ApiController]
[Route("api/crab")]
[Authorize]
[Tags("36. Crab Condition (Kn)")]
[Produces("application/json")]
public class CrabConditionController : ControllerBase
{
    private readonly ICrabConditionService _service;

    public CrabConditionController(ICrabConditionService service) => _service = service;

    /// <summary>[EVAL] Tính Kn — không ghi DB. Body: weightG + cwCm và/hoặc clCm.</summary>
    [HttpPost("condition")]
    public async Task<IActionResult> Evaluate(
        [FromBody] EvaluateCrabConditionRequest req, CancellationToken ct)
        => Ok(await _service.EvaluateAsync(req, ct));

    /// <summary>[EVAL] Batch Kn</summary>
    [HttpPost("condition/batch")]
    public async Task<IActionResult> EvaluateBatch(
        [FromBody] EvaluateCrabConditionBatchRequest req, CancellationToken ct)
        => Ok(await _service.EvaluateBatchAsync(req, ct));

    /// <summary>[CREATE] Tính Kn + lưu Inspection (type=condition) cho lịch sử mobile</summary>
    [HttpPost("condition/submit")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitCrabConditionRequest req, CancellationToken ct)
        => Ok(await _service.SubmitAsync(req, ct));

    /// <summary>[READ] Lịch sử condition theo box</summary>
    [HttpGet("condition/box/{boxId:guid}")]
    public async Task<IActionResult> ByBox(Guid boxId, CancellationToken ct)
        => Ok(await _service.ListByBoxAsync(boxId, ct));

    /// <summary>[READ] Condition record by id</summary>
    [HttpGet("condition/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    /// <summary>[EVAL] Daily check: Kn + DWG feed hint (growth_module)</summary>
    [HttpPost("daily-check")]
    public async Task<IActionResult> DailyCheck(
        [FromBody] DailyCheckRequest req, CancellationToken ct)
        => Ok(await _service.DailyCheckAsync(req, ct));
}

/// <summary>Alias routes matching mobile-friendly path /api/crab-condition</summary>
[ApiController]
[Route("api/crab-condition")]
[Authorize]
[Tags("36. Crab Condition (Kn)")]
[Produces("application/json")]
public class CrabConditionAliasController : ControllerBase
{
    private readonly ICrabConditionService _service;

    public CrabConditionAliasController(ICrabConditionService service) => _service = service;

    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate(
        [FromBody] EvaluateCrabConditionRequest req, CancellationToken ct)
        => Ok(await _service.EvaluateAsync(req, ct));

    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitCrabConditionRequest req, CancellationToken ct)
        => Ok(await _service.SubmitAsync(req, ct));

    [HttpGet("box/{boxId:guid}")]
    public async Task<IActionResult> ByBox(Guid boxId, CancellationToken ct)
        => Ok(await _service.ListByBoxAsync(boxId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));
}
