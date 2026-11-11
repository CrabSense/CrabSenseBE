using CrabSenseBE.Application.DTOs.FrozenStorage;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize(Roles = AppRoles.FarmPortal)]
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
    [Authorize(Roles = AppRoles.FarmManager)]
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
    [Authorize(Roles = AppRoles.FarmManager)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateFrozenLotRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateAsync(id, request, ct));
    }

    /// <summary>Xóa một lô cua cấp đông.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmManager)]
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

    /// <summary>
    /// Ghi nhận từng cua trong lô và tạo một barcode riêng cho mỗi cua.
    /// </summary>
    [HttpPost("{lotId:guid}/crab-items")]
    [Authorize(Roles = AppRoles.FarmStaff)]
    public async Task<IActionResult> RegisterCrabItems(
        Guid lotId,
        [FromBody] RegisterFrozenCrabItemsRequest request,
        CancellationToken ct)
        => Ok(await _service.RegisterCrabItemsAsync(lotId, request, ct));

    /// <summary>
    /// Tra cứu thông tin một cua cấp đông bằng barcode.
    /// Người gọi phải đăng nhập.
    /// </summary>
    [HttpGet("crab-items/barcode/{barcodeValue}")]
    [Authorize]
    public async Task<IActionResult> GetCrabItemByBarcode(
        string barcodeValue,
        CancellationToken ct)
        => Ok(await _service.GetCrabItemByBarcodeAsync(barcodeValue, ct));

    /// <summary>
/// Tạo ảnh PNG Code 128 để in barcode cho một cua cấp đông.
/// Route này yêu cầu đăng nhập do controller có [Authorize].
/// </summary>
[HttpGet("crab-items/barcode/{barcodeValue}/image.png")]
[Authorize]
[Produces("image/png")]
public async Task<IActionResult> GetCrabItemBarcodePng(
    string barcodeValue,
    CancellationToken ct)
{
    var bytes = await _service.GetCrabItemBarcodePngAsync(barcodeValue, ct);

    // Trình duyệt tải về ảnh PNG để người dùng in tem.
    return File(bytes, "image/png", $"crab-{barcodeValue}.png");
}

    /// <summary>Danh sách phiếu nhập/xuất kho trong khu được phân công.</summary>
    [HttpGet("~/api/frozen-storage-transactions")]
    public async Task<IActionResult> GetTransactions(CancellationToken ct)
        => Ok(await _service.GetTransactionsAsync(ct));

    /// <summary>FS lập phiếu nhập hoặc xuất kho; chưa cập nhật số tồn.</summary>
    [HttpPost("~/api/frozen-storage-transactions")]
    [Authorize(Roles = AppRoles.FarmStaff)]
    public async Task<IActionResult> CreateTransaction(
        [FromBody] CreateFrozenStorageTransactionRequest request,
        CancellationToken ct)
        => Ok(await _service.CreateTransactionAsync(request, ct));

    /// <summary>FM duyệt phiếu nhập/xuất; chưa cập nhật số tồn.</summary>
    [HttpPost("~/api/frozen-storage-transactions/{id:guid}/approve")]
    [Authorize(Roles = AppRoles.FarmManager)]
    public async Task<IActionResult> ApproveTransaction(Guid id, CancellationToken ct)
        => Ok(await _service.ApproveTransactionAsync(id, ct));

    /// <summary>FM từ chối phiếu nhập/xuất.</summary>
    [HttpPost("~/api/frozen-storage-transactions/{id:guid}/reject")]
    [Authorize(Roles = AppRoles.FarmManager)]
    public async Task<IActionResult> RejectTransaction(
        Guid id,
        [FromBody] RejectFrozenStorageTransactionRequest request,
        CancellationToken ct)
        => Ok(await _service.RejectTransactionAsync(id, request, ct));

    /// <summary>FS ghi số lượng/trọng lượng thực tế và cập nhật tồn kho đúng một lần.</summary>
    [HttpPost("~/api/frozen-storage-transactions/{id:guid}/execute")]
    [Authorize(Roles = AppRoles.FarmStaff)]
    public async Task<IActionResult> ExecuteTransaction(
        Guid id,
        [FromBody] ExecuteFrozenStorageTransactionRequest request,
        CancellationToken ct)
        => Ok(await _service.ExecuteTransactionAsync(id, request, ct));

    /// <summary>Nhật ký biến động tồn kho trong khu được phân công.</summary>
    [HttpGet("~/api/frozen-inventory-adjustments")]
    public async Task<IActionResult> GetAdjustments(
        [FromQuery] Guid? frozenLotId,
        CancellationToken ct)
        => Ok(await _service.GetAdjustmentsAsync(frozenLotId, ct));

    /// <summary>FM xem số dư đầu kỳ và trạng thái đối soát trong các khu được phân công.</summary>
    [HttpGet("~/api/frozen-opening-balances")]
    [Authorize(Roles = AppRoles.FarmManager)]
    public async Task<IActionResult> GetOpeningBalances(CancellationToken ct)
        => Ok(await _service.GetOpeningBalancesAsync(ct));

    /// <summary>FM xác nhận số dư đầu kỳ; không làm thay đổi tồn hiện tại.</summary>
    [HttpPost("~/api/frozen-opening-balances/{id:guid}/verify")]
    [Authorize(Roles = AppRoles.FarmManager)]
    public async Task<IActionResult> VerifyOpeningBalance(
        Guid id,
        [FromBody] VerifyFrozenOpeningBalanceRequest request,
        CancellationToken ct)
        => Ok(await _service.VerifyOpeningBalanceAsync(id, request, ct));

}
