using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Sales;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Luồng bán tổng hợp legacy cho mobile; không thay đổi tồn từng Crab.
/// SalesOrderService là nguồn sự thật cho bán cua có truy xuất và doanh thu theo đơn.
/// </summary>
public class SalesService : ISalesService
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;
    private readonly FarmAreaScope _areas;

    public SalesService(IUnitOfWork uow, ICurrentUserService currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public async Task<ApiResponse<SaleDto>> CreateAsync(CreateSaleRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.BuyerName))
            throw AppException.BadRequest("BuyerName is required.");
        if (req.Quantity <= 0)
            throw AppException.BadRequest("Quantity must be > 0.");
        if (req.UnitPrice < 0)
            throw AppException.BadRequest("UnitPrice must be >= 0.");

        Guid areaId;
        if (req.BoxId is Guid boxId && boxId != Guid.Empty)
        {
            var box = await _uow.Boxes.GetByIdAsync(boxId, ct)
                ?? throw AppException.NotFound("Box");
            areaId = await _areas.AreaIdOfBoxAsync(box, ct);
            if (req.FarmingAreaId is Guid suppliedArea
                && suppliedArea != Guid.Empty
                && suppliedArea != areaId)
                throw AppException.BadRequest(
                    "FarmingAreaId không khớp khu thực tế của hộp.");
        }
        else
        {
            areaId = req.FarmingAreaId ?? req.FarmId
                ?? throw AppException.BadRequest(
                    "FarmingAreaId hoặc BoxId là bắt buộc.");
        }
        await _areas.EnsureAsync(areaId, manage: false, ct);

        // TotalAmount từ client chỉ phục vụ tương thích DTO; backend là nguồn sự thật.
        var total = Math.Round(req.Quantity * req.UnitPrice, 2);
        var sale = new SaleTransaction
        {
            BuyerName = req.BuyerName.Trim(),
            BuyerContact = req.BuyerContact,
            Quantity = req.Quantity,
            UnitPrice = req.UnitPrice,
            TotalAmount = total,
            PaymentMethod = string.IsNullOrWhiteSpace(req.PaymentMethod) ? "CASH" : req.PaymentMethod.Trim().ToUpperInvariant(),
            PaymentStatus = string.IsNullOrWhiteSpace(req.PaymentStatus) ? "COMPLETED" : req.PaymentStatus.Trim().ToUpperInvariant(),
            SaleDate = req.SaleDate ?? DateTime.UtcNow,
            FarmingAreaId = areaId,
            BoxId = req.BoxId,
            OperatorId = _currentUser.UserId,
            OperatorName = req.OperatorName ?? "Operator",
            Notes = req.Notes
        };

        await _uow.SaleTransactions.AddAsync(sale, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<SaleDto>.Ok(
            Map(sale), "Đã ghi giao dịch tổng hợp legacy; không thay đổi tồn từng Crab.");
    }

    public async Task<ApiResponse<IEnumerable<SaleDto>>> GetHistoryAsync(
        Guid? farmId = null,
        string? buyerName = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? paymentMethod = null,
        string? paymentStatus = null,
        int page = 1,
        int limit = 50,
        CancellationToken ct = default)
    {
        var allowedAreas = await _areas.SelectionAsync(farmId, ct)
            ?? new HashSet<Guid>();
        var q = (await _uow.SaleTransactions.GetAllAsync(ct)).AsEnumerable();
        q = q.Where(s => s.FarmingAreaId is Guid id && allowedAreas.Contains(id));
        if (!string.IsNullOrWhiteSpace(buyerName))
            q = q.Where(s => s.BuyerName.Contains(buyerName, StringComparison.OrdinalIgnoreCase));
        if (startDate.HasValue) q = q.Where(s => s.SaleDate >= startDate.Value);
        if (endDate.HasValue) q = q.Where(s => s.SaleDate <= endDate.Value);
        if (!string.IsNullOrWhiteSpace(paymentMethod))
            q = q.Where(s => string.Equals(s.PaymentMethod, paymentMethod, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(paymentStatus))
            q = q.Where(s => string.Equals(s.PaymentStatus, paymentStatus, StringComparison.OrdinalIgnoreCase));

        var pageSize = limit <= 0 ? 50 : limit;
        var pageNum = page <= 0 ? 1 : page;
        var items = q.OrderByDescending(s => s.SaleDate)
            .Skip((pageNum - 1) * pageSize)
            .Take(pageSize)
            .Select(Map)
            .ToList();
        return ApiResponse<IEnumerable<SaleDto>>.Ok(items);
    }

    public async Task<ApiResponse<SalesSummaryDto>> GetSummaryAsync(
        DateTime startDate, DateTime endDate, Guid? farmId = null, CancellationToken ct = default)
    {
        var allowedAreas = await _areas.SelectionAsync(farmId, ct)
            ?? new HashSet<Guid>();
        var q = (await _uow.SaleTransactions.GetAllAsync(ct)).AsEnumerable()
            .Where(s => s.SaleDate >= startDate
                && s.SaleDate <= endDate
                && s.FarmingAreaId is Guid id
                && allowedAreas.Contains(id));

        var list = q.ToList();
        return ApiResponse<SalesSummaryDto>.Ok(new SalesSummaryDto(
            list.Sum(s => s.TotalAmount),
            list.Sum(s => s.Quantity),
            list.Count,
            startDate,
            endDate));
    }

    public async Task<ApiResponse<SaleDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var sale = await _uow.SaleTransactions.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Sale");
        if (sale.FarmingAreaId is not Guid areaId)
            throw AppException.Forbidden("Giao dịch chưa xác định khu sở hữu.");
        await _areas.EnsureAsync(areaId, manage: false, ct);
        return ApiResponse<SaleDto>.Ok(Map(sale));
    }

    public async Task<ApiResponse<object>> GetAvailableInventoryAsync(
        Guid? farmId = null, CancellationToken ct = default)
    {
        var allowedAreas = await _areas.SelectionAsync(farmId, ct)
            ?? new HashSet<Guid>();
        var rows = (await _uow.FarmingRows.GetAllAsync(ct))
            .ToDictionary(row => row.Id);
        var boxes = (await _uow.Boxes.GetAllAsync(ct))
            .Where(box => rows.TryGetValue(box.FarmingRowId, out var row)
                && allowedAreas.Contains(row.FarmingAreaId))
            .Select(box => box.Id)
            .ToHashSet();
        var lastBoxes = (await _uow.CrabBoxAllocations.GetAllAsync(ct))
            .GroupBy(allocation => allocation.CrabId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(allocation => allocation.StartTime)
                    .First().BoxId);
        var crabs = (await _uow.Crabs.GetAllAsync(ct)).AsEnumerable()
            .Where(c => c.Status == CrabStatus.Harvested
                && boxes.Contains(
                    c.BoxId
                    ?? lastBoxes.GetValueOrDefault(c.Id)));
        // Approximate kg available from harvested weight grams.
        var kg = crabs.Where(c => c.WeightGram.HasValue).Sum(c => c.WeightGram!.Value) / 1000m;
        return ApiResponse<object>.Ok(new { availableKg = Math.Round(kg, 2), farmId });
    }

    private static SaleDto Map(SaleTransaction s) => new(
        s.Id, s.BuyerName, s.BuyerContact, s.Quantity, s.UnitPrice, s.TotalAmount,
        s.PaymentMethod, s.PaymentStatus, s.SaleDate, s.FarmingAreaId, s.BoxId,
        s.OperatorId, s.OperatorName, s.Notes, s.CreatedAt);
}

public class BoxCameraService : IBoxCameraService
{
    private readonly IUnitOfWork _uow;
    private readonly FarmAreaScope _areas;

    public BoxCameraService(IUnitOfWork uow, ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public async Task<ApiResponse<BoxCameraDto>> GetCameraForBoxAsync(Guid boxId, CancellationToken ct = default)
    {
        await _areas.EnsureBoxAsync(boxId, manage: false, ct);
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct) ?? throw AppException.NotFound("Box");
        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);

        // Ưu tiên camera gắn đúng dãy của hộp → camera tổng quan khu → camera bất kỳ mới hoạt động nhất.
        var cameras = (await _uow.Devices.GetAllAsync(ct))
            .Where(CameraConfiguration.IsCamera)
            .OrderByDescending(d => d.LastSeenAt ?? d.CreatedAt)
            .ToList();
        var camera = cameras.FirstOrDefault(d => d.FarmingRowId == box.FarmingRowId)
            ?? (row is null ? null : cameras.FirstOrDefault(d => d.FarmingAreaId == row.FarmingAreaId));

        // Latest box media as real preview (Drive/web link) when camera stream is unavailable.
        var latestMedia = (await _uow.MediaAssets.FindAsync(
                m => m.BoxId == boxId && (m.Category == "video" || m.Category == "image"), ct))
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();
        var preview = latestMedia?.WebViewLink ?? latestMedia?.StorageKey;

        if (camera is null)
        {
            return ApiResponse<BoxCameraDto>.Ok(new BoxCameraDto(
                boxId,
                null,
                latestMedia is null ? "none" : "media-preview",
                latestMedia is null ? "offline" : "preview",
                StreamUrl: preview,
                SnapshotUrl: preview,
                LastSeenAt: latestMedia?.CreatedAt,
                Message: latestMedia is null
                    ? "Chưa có camera đăng ký — quay video AI hoặc gắn device type=camera."
                    : "Chưa có camera live — đang hiện media gần nhất của hộp."));
        }

        var online = CameraConfiguration.Status(camera, DateTime.UtcNow) == "Online";

        var stream = CameraConfiguration.SafeUrl(camera.StreamUrl);
        var snapshot = CameraConfiguration.SafeUrl(camera.SnapshotUrl, snapshot: true);
        return ApiResponse<BoxCameraDto>.Ok(new BoxCameraDto(
            boxId,
            camera.Id,
            camera.DeviceCode,
            CameraConfiguration.Status(camera, DateTime.UtcNow).ToLowerInvariant(),
            stream ?? preview,
            snapshot ?? preview,
            camera.LastSeenAt ?? latestMedia?.CreatedAt,
            online
                ? "Camera có heartbeat gần đây; luồng video chưa được xác minh."
                : (preview != null
                    ? "Camera offline — đang hiện media gần nhất của hộp."
                    : "Camera offline hoặc mất kết nối.")));
    }
}
