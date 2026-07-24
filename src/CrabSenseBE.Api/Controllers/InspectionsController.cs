using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/inspections")]
[Authorize]
[Tags("35. Manual Inspections")]
[Produces("application/json")]
public class InspectionsController : ControllerBase
{
    private readonly IManualInspectionService _service;

    public InspectionsController(IManualInspectionService service) => _service = service;

    /// <summary>[CREATE] Submit manual inspection (Mobile)</summary>
    [HttpPost("submit")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitManualInspectionRequest req, CancellationToken ct = default)
        => Ok(await _service.SubmitAsync(req, ct));

    /// <summary>[CREATE] Alias POST /api/inspections</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create(
        [FromBody] SubmitManualInspectionRequest req, CancellationToken ct = default)
        => Ok(await _service.SubmitAsync(req, ct));

    /// <summary>[READ] Inspections for a box</summary>
    [HttpGet("box/{boxId:guid}")]
    public async Task<IActionResult> ByBox(Guid boxId, CancellationToken ct = default)
        => Ok(await _service.ListByBoxAsync(boxId, ct));

    /// <summary>[READ] Inspection by id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
        => Ok(await _service.GetByIdAsync(id, ct));
}
