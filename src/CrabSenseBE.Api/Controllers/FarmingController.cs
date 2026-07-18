using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/farming-areas")]
[Authorize]
[Produces("application/json")]
public class FarmingAreasController : ControllerBase
{
    private readonly IFarmingService _service;
    public FarmingAreasController(IFarmingService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetAreasAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetAreaByIdAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Create([FromBody] CreateFarmingAreaRequest req, CancellationToken ct)
        => Ok(await _service.CreateAreaAsync(req, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFarmingAreaRequest req, CancellationToken ct)
        => Ok(await _service.UpdateAreaAsync(id, req, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteAreaAsync(id, ct));

    // ─── Nested: Rows
    [HttpGet("{areaId:guid}/rows")]
    public async Task<IActionResult> GetRows(Guid areaId, CancellationToken ct)
        => Ok(await _service.GetRowsByAreaAsync(areaId, ct));

    [HttpPost("{areaId:guid}/rows")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> CreateRow(Guid areaId, [FromBody] CreateFarmingRowRequest req, CancellationToken ct)
        => Ok(await _service.CreateRowAsync(req with { FarmingAreaId = areaId }, ct));
}

[ApiController]
[Route("api/farming-rows/{rowId:guid}/boxes")]
[Authorize]
[Produces("application/json")]
public class BoxesController : ControllerBase
{
    private readonly IFarmingService _service;
    public BoxesController(IFarmingService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetByRow(Guid rowId, CancellationToken ct)
        => Ok(await _service.GetBoxesByRowAsync(rowId, ct));

    [HttpPost]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Create(Guid rowId, [FromBody] CreateBoxRequest req, CancellationToken ct)
        => Ok(await _service.CreateBoxAsync(req with { FarmingRowId = rowId }, ct));
}

[ApiController]
[Route("api/crabs")]
[Authorize]
[Produces("application/json")]
public class CrabsController : ControllerBase
{
    private readonly IFarmingService _service;
    public CrabsController(IFarmingService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _service.GetCrabsAsync(page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetCrabByIdAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Create([FromBody] CreateCrabRequest req, CancellationToken ct)
        => Ok(await _service.CreateCrabAsync(req, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCrabRequest req, CancellationToken ct)
        => Ok(await _service.UpdateCrabAsync(id, req, ct));
}
