using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize]
[Tags("04b. History — Allocation & Molting")]
[Produces("application/json")]
public class FarmHistoryController : ControllerBase
{
    private readonly IFarmHistoryService _service;
    public FarmHistoryController(IFarmHistoryService service) => _service = service;

    /// <summary>[CREATE] Allocate / move crab into a box (closes previous allocation)</summary>
    [HttpPost("api/allocations")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Allocate([FromBody] AllocateCrabRequest req, CancellationToken ct)
        => Ok(await _service.AllocateCrabAsync(req, ct));

    /// <summary>[CREATE] Mobile transfer alias — crabId + destinationBoxId (+ sourceBoxId)</summary>
    [HttpPost("api/allocations/transfer")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Transfer([FromBody] MobileTransferCrabRequest req, CancellationToken ct)
        => Ok(await _service.TransferCrabAsync(req, ct));

    /// <summary>[UPDATE] Fix allocation notes / times (mistake correction)</summary>
    [HttpPut("api/allocations/{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> UpdateAllocation(
        Guid id, [FromBody] UpdateAllocationRequest req, CancellationToken ct)
        => Ok(await _service.UpdateAllocationAsync(id, req, ct));

    /// <summary>[DELETE] Delete a closed allocation record</summary>
    [HttpDelete("api/allocations/{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> DeleteAllocation(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteAllocationAsync(id, ct));

    /// <summary>[READ] Allocation history by crab (optional from/to)</summary>
    [HttpGet("api/crabs/{crabId:guid}/allocations")]
    public async Task<IActionResult> AllocationsByCrab(
        Guid crabId, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default)
        => Ok(await _service.GetAllocationsByCrabAsync(crabId, from, to, ct));

    /// <summary>[READ] Allocation / farming history by box (optional from/to)</summary>
    [HttpGet("api/boxes/{boxId:guid}/allocations")]
    public async Task<IActionResult> AllocationsByBox(
        Guid boxId, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default)
        => Ok(await _service.GetAllocationsByBoxAsync(boxId, from, to, ct));

    /// <summary>[CREATE] Record molting event (lịch sử lột xác)</summary>
    [HttpPost("api/crabs/{crabId:guid}/moltings")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> CreateMolting(
        Guid crabId, [FromBody] CreateMoltingRecordRequest req, CancellationToken ct)
        => Ok(await _service.CreateMoltingAsync(req with { CrabId = crabId }, ct));

    /// <summary>[UPDATE] Fix a molting record (mistake correction)</summary>
    [HttpPut("api/moltings/{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> UpdateMolting(
        Guid id, [FromBody] UpdateMoltingRecordRequest req, CancellationToken ct)
        => Ok(await _service.UpdateMoltingAsync(id, req, ct));

    /// <summary>[DELETE] Delete a molting record (resync crab stage/weight from remaining)</summary>
    [HttpDelete("api/moltings/{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> DeleteMolting(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteMoltingAsync(id, ct));

    /// <summary>[READ] Molting history by crab (optional from/to)</summary>
    [HttpGet("api/crabs/{crabId:guid}/moltings")]
    public async Task<IActionResult> MoltingsByCrab(
        Guid crabId, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default)
        => Ok(await _service.GetMoltingsByCrabAsync(crabId, from, to, ct));

    /// <summary>[READ] Molting history by box (optional from/to)</summary>
    [HttpGet("api/boxes/{boxId:guid}/moltings")]
    public async Task<IActionResult> MoltingsByBox(
        Guid boxId, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default)
        => Ok(await _service.GetMoltingsByBoxAsync(boxId, from, to, ct));

    /// <summary>[READ] Live farming status of one box (status + current crab + counts)</summary>
    [HttpGet("api/boxes/{boxId:guid}/farming-status")]
    public async Task<IActionResult> BoxFarmingStatus(Guid boxId, CancellationToken ct)
        => Ok(await _service.GetBoxFarmingStatusAsync(boxId, ct));

    /// <summary>[READ] Farming status of many boxes — filter area/row/status</summary>
    [HttpGet("api/boxes/farming-status")]
    public async Task<IActionResult> ListBoxesFarmingStatus(
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] Guid? farmingRowId = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
        => Ok(await _service.ListBoxesFarmingStatusAsync(farmingAreaId, farmingRowId, status, ct));

    /// <summary>[READ] Box status change history (audit)</summary>
    [HttpGet("api/boxes/{boxId:guid}/status-history")]
    public async Task<IActionResult> BoxStatusHistory(
        Guid boxId, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default)
        => Ok(await _service.GetBoxStatusHistoryAsync(boxId, from, to, ct));

    /// <summary>[UPDATE] Fix a box status history row</summary>
    [HttpPut("api/box-status-histories/{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> UpdateBoxStatusHistory(
        Guid id, [FromBody] UpdateBoxStatusHistoryRequest req, CancellationToken ct)
        => Ok(await _service.UpdateBoxStatusHistoryAsync(id, req, ct));

    /// <summary>[DELETE] Delete a box status history row (does not revert live box status)</summary>
    [HttpDelete("api/box-status-histories/{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> DeleteBoxStatusHistory(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteBoxStatusHistoryAsync(id, ct));

    /// <summary>[READ] Combined farming timeline for a box (allocation + molt + status)</summary>
    [HttpGet("api/boxes/{boxId:guid}/farming-timeline")]
    public async Task<IActionResult> BoxFarmingTimeline(
        Guid boxId, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default)
        => Ok(await _service.GetBoxFarmingTimelineAsync(boxId, from, to, ct));
}
