using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/crab-lots")]
[Authorize]
[Tags("05. CRUD — Crab Lots")]
[Produces("application/json")]
public class CrabLotsController : ControllerBase
{
    private readonly IFarmLotService _service;
    public CrabLotsController(IFarmLotService service) => _service = service;

    /// <summary>[READ] List all crab lots</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetLotsAsync(ct));

    /// <summary>[READ] Get crab lot by id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetLotByIdAsync(id, ct));

    /// <summary>[CREATE] Create crab lot</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create([FromBody] CreateCrabLotRequest req, CancellationToken ct)
        => Ok(await _service.CreateLotAsync(req, ct));

    /// <summary>[UPDATE] Update crab lot</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCrabLotRequest req, CancellationToken ct)
        => Ok(await _service.UpdateLotAsync(id, req, ct));

    /// <summary>[DELETE] Delete crab lot (no crabs referencing it)</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteLotAsync(id, ct));
}

[ApiController]
[Route("api/crop-batches")]
[Authorize]
[Tags("06. CRUD — Crop Batches")]
[Produces("application/json")]
public class CropBatchesController : ControllerBase
{
    private readonly IFarmLotService _service;
    public CropBatchesController(IFarmLotService service) => _service = service;

    /// <summary>[READ] List all crop batches</summary>
    // [HttpGet]
    // public async Task<IActionResult> GetAll(CancellationToken ct)
    //     => Ok(await _service.GetBatchesAsync(ct));

    /// <summary>[CREATE] Create crop batch</summary>
    // [HttpPost]
    // [Authorize(Roles = AppRoles.FarmWrite)]
    // public async Task<IActionResult> Create([FromBody] CreateCropBatchRequest req, CancellationToken ct)
    //     => Ok(await _service.CreateBatchAsync(req, ct));

    /// <summary>[UPDATE] Update crop batch</summary>
//     [HttpPut("{id:guid}")]
//     [Authorize(Roles = AppRoles.FarmWrite)]
//     public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCropBatchRequest req, CancellationToken ct)
//         => Ok(await _service.UpdateBatchAsync(id, req, ct));

//     /// <summary>[DELETE] Delete crop batch / vụ nuôi (no crabs referencing it)</summary>
//     [HttpDelete("{id:guid}")]
//     [Authorize(Roles = AppRoles.FarmWrite)]
//     public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
//         => Ok(await _service.DeleteBatchAsync(id, ct));
}
