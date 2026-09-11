using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Quản lý phiếu thu hoạch và thống kê sản lượng.
/// </summary>
[ApiController]
[Route("api/harvest-vouchers")]
[Authorize]
[Tags("20. CRUD — Harvest")]
[Produces("application/json")]
public class HarvestController : ControllerBase
{
    private readonly IHarvestService _harvestService;

    public HarvestController(IHarvestService harvestService)
    {
        _harvestService = harvestService;
    }

    /// <summary>
    /// [READ] Lấy danh sách phiếu thu hoạch.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? farmingAreaId,
        CancellationToken ct)
    {
        var result = await _harvestService.GetAllAsync(farmingAreaId, ct);

        return Ok(result);
    }

    /// <summary>[READ] KPI thu hoạch: có thể thu, hôm nay, chờ bán.</summary>
    [HttpGet("overview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Overview(
        [FromQuery] Guid? farmingAreaId,
        CancellationToken ct)
        => Ok(await _harvestService.GetOverviewAsync(farmingAreaId, ct));

    /// <summary>
    /// [READ] Lấy chi tiết một phiếu thu hoạch.
    /// </summary>
    /// <param name="id">ID phiếu thu hoạch.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken ct)
    {
        var result = await _harvestService.GetByIdAsync(id, ct);

        return Ok(result);
    }

    /// <summary>
    /// [CREATE] Tạo phiếu thu hoạch mới.
    /// </summary>
    /// <remarks>
    /// Backend tự tính:
    /// - Tổng số lượng
    /// - Tổng khối lượng
    /// - Số lượng cua lột
    /// - Tỷ lệ cua lột
    ///
    /// CreatedBy được lấy từ JWT, không truyền trong request body.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateHarvestVoucherRequest request,
        CancellationToken ct)
    {
        var result = await _harvestService.CreateAsync(request, ct);

        return Ok(result);
    }

    /// <summary>
    /// [ACTION] Cập nhật trạng thái phiếu thu hoạch.
    /// </summary>
    /// <param name="id">ID phiếu thu hoạch.</param>
    /// <remarks>
    /// Luồng trạng thái hợp lệ:
    ///
    /// Planned → InProgress → Completed
    ///
    /// Planned → Cancelled
    ///
    /// InProgress → Cancelled
    /// </remarks>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateHarvestStatusRequest request,
        CancellationToken ct)
    {
        var result = await _harvestService.UpdateStatusAsync(
            id,
            request,
            ct);

        return Ok(result);
    }

    /// <summary>
    /// [DELETE] Xóa phiếu thu hoạch.
    /// </summary>
    /// <param name="id">ID phiếu thu hoạch.</param>
    /// <remarks>
    /// Chỉ cho phép xóa phiếu có trạng thái Planned hoặc Cancelled
    /// và chưa được sử dụng để tạo lô cấp đông.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken ct)
    {
        var result = await _harvestService.DeleteAsync(id, ct);

        return Ok(result);
    }

    /// <summary>
    /// [READ] Thống kê thu hoạch theo ngày, tuần hoặc tháng.
    /// </summary>
    /// <param name="from">
    /// Thời điểm bắt đầu, bao gồm trong kết quả.
    /// </param>
    /// <param name="to">
    /// Thời điểm kết thúc, không bao gồm trong kết quả.
    /// </param>
    /// <param name="period">
    /// Kiểu nhóm dữ liệu: day, week hoặc month.
    /// </param>
    /// <example>
    /// GET /api/harvest-vouchers/statistics
    /// ?from=2026-07-01T00:00:00Z
    /// &amp;to=2026-08-01T00:00:00Z
    /// &amp;period=day
    /// </example>
    [HttpGet("statistics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] string period = "day",
        CancellationToken ct = default)
    {
        var result = await _harvestService.GetStatisticsAsync(
            from,
            to,
            period,
            ct);

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Box ↔ Harvest link
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// [READ] Danh sách box tham chiếu trong phiếu thu hoạch.
    /// </summary>
    /// <param name="id">ID phiếu thu hoạch.</param>
    /// <remarks>
    /// Trả về danh sách box (BoxId, BoxCode) đã được ghi nhận trong các dòng
    /// thu hoạch, kèm số lượng cua và tổng khối lượng từng box.
    /// </remarks>
    [HttpGet("{id:guid}/boxes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBoxesByVoucher(
        Guid id,
        CancellationToken ct)
    {
        var result = await _harvestService.GetBoxesByVoucherAsync(id, ct);

        return Ok(result);
    }

    /// <summary>
    /// [READ] Danh sách phiếu thu hoạch tham chiếu đến một box.
    /// </summary>
    /// <param name="boxId">ID box nuôi.</param>
    /// <remarks>
    /// Trả về tất cả phiếu thu hoạch đã ghi nhận cua từ box này,
    /// kèm số lượng cua và tổng khối lượng từng phiếu.
    /// </remarks>
    [HttpGet("~/api/boxes/{boxId:guid}/harvest-vouchers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVouchersByBox(
        Guid boxId,
        CancellationToken ct)
    {
        var result = await _harvestService.GetVouchersByBoxAsync(boxId, ct);

        return Ok(result);
    }
}
