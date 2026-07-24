using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Sales;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class SalesService : ISalesService
{
    private readonly IUnitOfWork _uow;

    public SalesService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<SaleDto>> CreateAsync(CreateSaleRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.BuyerName))
            throw AppException.BadRequest("BuyerName is required.");
        if (req.Quantity <= 0)
            throw AppException.BadRequest("Quantity must be > 0.");
        if (req.UnitPrice < 0)
            throw AppException.BadRequest("UnitPrice must be >= 0.");

        var total = req.TotalAmount ?? Math.Round(req.Quantity * req.UnitPrice, 2);
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
            FarmingAreaId = req.FarmingAreaId ?? req.FarmId,
            BoxId = req.BoxId,
            OperatorId = req.OperatorId ?? Guid.Empty,
            OperatorName = req.OperatorName ?? "Operator",
            Notes = req.Notes
        };

        await _uow.SaleTransactions.AddAsync(sale, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<SaleDto>.Ok(Map(sale), "Created.");
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
        var q = (await _uow.SaleTransactions.GetAllAsync(ct)).AsEnumerable();
        if (farmId is Guid fid && fid != Guid.Empty)
            q = q.Where(s => s.FarmingAreaId == fid);
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
        var q = (await _uow.SaleTransactions.GetAllAsync(ct)).AsEnumerable()
            .Where(s => s.SaleDate >= startDate && s.SaleDate <= endDate);
        if (farmId is Guid fid && fid != Guid.Empty)
            q = q.Where(s => s.FarmingAreaId == fid);

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
        return ApiResponse<SaleDto>.Ok(Map(sale));
    }

    public async Task<ApiResponse<object>> GetAvailableInventoryAsync(
        Guid? farmId = null, CancellationToken ct = default)
    {
        var crabs = (await _uow.Crabs.GetAllAsync(ct)).AsEnumerable()
            .Where(c => c.Status == CrabStatus.Harvested);
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

    public BoxCameraService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<BoxCameraDto>> GetCameraForBoxAsync(Guid boxId, CancellationToken ct = default)
    {
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct) ?? throw AppException.NotFound("Box");
        _ = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);

        var devices = (await _uow.Devices.GetAllAsync(ct)).ToList();
        var camera = devices
            .Where(d => d.DeviceType.Contains("camera", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.LastSeenAt ?? d.CreatedAt)
            .FirstOrDefault();

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

        var online = camera.Status == DeviceStatus.Online
            || (camera.LastSeenAt.HasValue && camera.LastSeenAt > DateTime.UtcNow.AddMinutes(-15));

        // Prefer configured stream from firmware notes when present: "rtsp://..." or "http...m3u8"
        string? stream = null;
        if (!string.IsNullOrWhiteSpace(camera.FirmwareVersion) &&
            (camera.FirmwareVersion.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
             camera.FirmwareVersion.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
             camera.FirmwareVersion.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
        {
            stream = camera.FirmwareVersion.Trim();
        }
        else if (online)
        {
            // Dev-friendly HLS template; replace FirmwareVersion with real RTSP/HLS to go live.
            stream = $"https://stream.crabsense.local/live/{Uri.EscapeDataString(camera.DeviceCode)}.m3u8?boxId={boxId}";
        }

        // Always expose a real snapshot/preview when we have media.
        var snapshot = preview;

        return ApiResponse<BoxCameraDto>.Ok(new BoxCameraDto(
            boxId,
            camera.Id,
            camera.DeviceCode,
            online ? "online" : camera.Status.ToString().ToLowerInvariant(),
            stream ?? preview,
            snapshot,
            camera.LastSeenAt ?? latestMedia?.CreatedAt,
            online
                ? (stream != null && stream.Contains("stream.crabsense.local", StringComparison.OrdinalIgnoreCase)
                    ? "Camera online — đặt FirmwareVersion = RTSP/HLS URL thật để phát live."
                    : "Camera online — stream sẵn sàng.")
                : (preview != null
                    ? "Camera offline — đang hiện media gần nhất của hộp."
                    : "Camera offline hoặc mất kết nối.")));
    }
}
