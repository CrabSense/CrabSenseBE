using CrabSenseBE.Application.DTOs.FrozenStorage;
using CrabSenseBE.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/frozen-lots")]
[Tags("Frozen Storage")]
public class FrozenStorageController : ControllerBase
{
    private readonly IFrozenStorageService _service;

    public FrozenStorageController(IFrozenStorageService service)
    {
        _service = service;
    }

    /// <summary>Lấy toàn bộ lô cua cấp đông.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        return Ok(await _service.GetAllAsync(ct));
    }

    /// <summary>Lấy chi tiết một lô cua cấp đông.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken ct)
    {
        return Ok(await _service.GetByIdAsync(id, ct));
    }

    /// <summary>Tạo một lô cua cấp đông.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateFrozenLotRequest request,
        CancellationToken ct)
    {
        var response = await _service.CreateAsync(request, ct);

        return CreatedAtAction(
            nameof(GetById),
            new { id = response.Data!.Id },
            response);
    }

    /// <summary>Cập nhật một lô cua cấp đông.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateFrozenLotRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateAsync(id, request, ct));
    }

    /// <summary>Xóa một lô cua cấp đông.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken ct)
    {
        return Ok(await _service.DeleteAsync(id, ct));
    }


    /// <summary>Lấy thống kê tổng hợp tồn kho cấp đông.</summary>
    [HttpGet("inventory-summary")]
    public async Task<IActionResult> GetInventorySummary(
        CancellationToken ct)
    {
        return Ok(await _service.GetInventorySummaryAsync(ct));
    }

    /// <summary>
    /// Lấy danh sách lô cấp đông kèm thời gian đã lưu
    /// và số ngày còn lại trước khi hết hạn.
    /// </summary>
    [HttpGet("storage-aging")]
    public async Task<IActionResult> GetStorageAging(
        CancellationToken ct)
    {
        return Ok(await _service.GetStorageAgingAsync(ct));
    }

    /// <summary>
    /// Lấy danh sách lô cấp đông sắp hết thời gian bảo quản.
    /// </summary>
    [HttpGet("expiring")]
    public async Task<IActionResult> GetExpiringLots(
        [FromQuery] int days = 30,
        CancellationToken ct = default)
    {
        return Ok(await _service.GetExpiringLotsAsync(days, ct));
    }

}
