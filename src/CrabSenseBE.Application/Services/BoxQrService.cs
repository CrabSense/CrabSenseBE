using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using QRCoder;
using CrabSenseBE.Domain.Enums;


namespace CrabSenseBE.Application.Services;

/// <summary>
/// Nghiệp vụ QR hộp tại trại.
/// Luồng mobile: quét tem → ScanAsync → xem/sửa cua → MoveCrabAsync khi đổi hộp.
/// </summary>
public class BoxQrService : IBoxQrService
{
    private readonly IUnitOfWork _uow;
    private readonly IFarmHistoryService _history;
    private readonly FarmAreaScope _areas;

    public BoxQrService(
        IUnitOfWork uow,
        IFarmHistoryService history,
        ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _history = history;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public async Task<ApiResponse<BoxQrDto>> EnsureBoxQrAsync(Guid boxId, CancellationToken ct = default)
    {
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");
        await _areas.EnsureBoxAsync(boxId, manage: true, ct);

        // Đã có QR active cho box → trả về luôn
        var existing = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.BoxId == boxId && q.IsActive && q.EntityType == "box", ct);
        if (existing is not null)
            return ApiResponse<BoxQrDto>.Ok(MapQr(existing));

        // Prefer stable sticker code = box.Code (mobile can also send CRABSENSE:BOX:{code}).
        var code = box.Code.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            var shortId = boxId.ToString("N")[..8].ToUpperInvariant();
            code = $"BOX-{shortId}";
        }

        if (await _uow.QrCodes.AnyAsync(q => q.Code == code, ct))
        {
            var shortId = boxId.ToString("N")[..8].ToUpperInvariant();
            code = $"{box.Code}-{shortId}";
        }

        var qr = new QrCode
        {
            Code = code,
            EntityType = "box",
            BoxId = boxId,
            IsActive = true,
            ApprovalStatus = QrApprovalStatus.Approved,
            Payload = JsonSerializer.Serialize(new
            {
                type = "box",
                boxId,
                boxCode = box.Code,
                crabsense = $"CRABSENSE:BOX:{box.Code}",
                path = $"/scan/box/{code}"
            })
        };
        await _uow.QrCodes.AddAsync(qr, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<BoxQrDto>.Ok(MapQr(qr), "QR created.");
    }

    public async Task<ApiResponse<BoxQrDto>> GetQrByBoxAsync(Guid boxId, CancellationToken ct = default)
    {
        await _areas.EnsureBoxAsync(boxId, manage: false, ct);
        var qr = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.BoxId == boxId && q.IsActive && q.EntityType == "box", ct);
        if (qr is null)
            throw AppException.NotFound("QrCode");
        return ApiResponse<BoxQrDto>.Ok(MapQr(qr));
    }

    public async Task<byte[]> GetBoxQrPngAsync(Guid boxId, int pixelsPerModule = 8, CancellationToken ct = default)
    {
        var result = await GetQrByBoxAsync(boxId, ct);
        var code = result.Data?.Code
            ?? throw AppException.NotFound("QrCode");

        var ppm = Math.Clamp(pixelsPerModule, 4, 20);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        // quiet zone keeps sticker scannable
        return png.GetGraphic(ppm, drawQuietZones: true);
    }

    public async Task<ApiResponse<BoxScanResultDto>> ScanAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw AppException.BadRequest("QR code is required.");

        var normalized = code.Trim();
        // Accept CRABSENSE:BOX:<code> stickers from mobile.
        if (normalized.StartsWith("CRABSENSE:BOX:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["CRABSENSE:BOX:".Length..].Trim();

        var qr = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.Code == normalized && q.IsActive, ct);

        if (qr is null)
            throw AppException.NotFound("QrCode");

        if (qr.EntityType != "box" || qr.BoxId is null)
            throw AppException.BadRequest("QR này không gắn với hộp nuôi.");

        var box = await _uow.Boxes.GetByIdAsync(qr.BoxId.Value, ct)
            ?? throw AppException.NotFound("Box");
        await _areas.EnsureBoxAsync(box.Id, manage: false, ct);

        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
        var areaName = "";
        var rowName = row?.Name ?? "";
        if (row is not null)
        {
            var area = await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);
            areaName = area?.Name ?? "";
        }

        // Cua đang trong hộp
        var allocs = await _uow.CrabBoxAllocations.FindAsync(
    a => a.BoxId == box.Id && a.EndTime == null, ct);
        var crabIds = allocs.Select(a => a.CrabId).ToList();
        var allCrabs = await _uow.Crabs.FindAsync(c => crabIds.Contains(c.Id), ct);
        var crabs = allCrabs.Where(c =>
            c.Status == CrabStatus.Alive || c.Status == CrabStatus.Molting || c.Status == CrabStatus.Quarantined
        ).ToList();

        // Thời điểm bắt đầu allocation hiện tại (EndTime == null)
        var openAllocs = await _uow.CrabBoxAllocations.FindAsync(
            a => a.BoxId == box.Id && a.EndTime == null, ct);
        var allocByCrab = openAllocs
            .GroupBy(a => a.CrabId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartTime).First().StartTime);

        var crabDtos = crabs.Select(c => new BoxScanCrabDto(
    c.Id, c.Tag, c.WeightGram,
    c.MoltingStage,
    c.Status == CrabStatus.Alive || c.Status == CrabStatus.Molting || c.Status == CrabStatus.Quarantined,
    c.MoltedAt, c.CrabLotId,
    allocByCrab.TryGetValue(c.Id, out var start) ? start : null,
    JsonStringList.Parse(c.ImageUrlsJson)
)).ToList();

        // Đếm số lần quét (analytics hiện trường)
        qr.ScanCount += 1;
        _uow.QrCodes.Update(qr);
        await _uow.SaveChangesAsync(ct);

        var result = new BoxScanResultDto(
            MapQr(qr),
            new BoxScanBoxDto(box.Id, box.Code, box.Status, box.IsOccupied, box.FarmingRowId),
            crabDtos,
            areaName,
            rowName);

        return ApiResponse<BoxScanResultDto>.Ok(result);
    }

    public async Task<ApiResponse<CrabDto>> UpdateCrabFromScanAsync(
        string boxQrCode, Guid crabId, UpdateCrabFromScanRequest req, CancellationToken ct = default)
    {
        var qr = await RequireBoxQrAsync(boxQrCode, ct);
        await _areas.EnsureBoxAsync(qr.BoxId!.Value, manage: false, ct);
        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct)
            ?? throw AppException.NotFound("Crab");

        // Chỉ cho sửa cua đang thuộc hộp của tem vừa quét (tránh nhầm hộp)
        if (GetCrabBoxId(crab) != qr.BoxId)
            throw AppException.BadRequest("Cua không thuộc hộp của mã QR này.");
        if (crab.Status is CrabStatus.Dead or CrabStatus.Harvested or CrabStatus.Sold)
            throw AppException.Conflict(
                "Cua đã có trạng thái cuối cùng; chỉ được sửa qua quy trình correction có audit.");
        if (req.IsAlive.HasValue)
            throw AppException.BadRequest(
                "Không đổi trạng thái sống/chết qua QR. Dùng API mortality hoặc correction tương ứng.");
        if (req.MoltedAt.HasValue || req.MoltingStage is not null)
            throw AppException.BadRequest(
                "Không ghi nhận lột xác qua cập nhật QR chung. Dùng API molting để giữ lịch sử.");

        var previousWeight = crab.WeightGram;
        if (req.Tag is not null) crab.Tag = req.Tag;
        if (req.WeightGram.HasValue) crab.WeightGram = req.WeightGram;

        _uow.Crabs.Update(crab);
        if (req.WeightGram is decimal nextWeight && nextWeight != previousWeight)
        {
            await _uow.CrabWeightHistories.AddAsync(new CrabWeightHistory
            {
                CrabId = crab.Id,
                WeightGram = nextWeight,
                CarapaceWidthMm = crab.CarapaceWidthMm,
                CarapaceLengthMm = crab.CarapaceLengthMm,
                Source = "box-qr"
            }, ct);
        }
        await _uow.SaveChangesAsync(ct);

        var boxId = GetCrabBoxId(crab);
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
        var row = box is null ? null : await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);

        return ApiResponse<CrabDto>.Ok(new CrabDto(
            crab.Id,
            boxId,
            box?.Code,
            box?.FarmingRowId ?? Guid.Empty,
            row?.FarmingAreaId ?? Guid.Empty,
            crab.CrabLotId,
            crab.Tag,
            crab.WeightGram,
            crab.MoltingStage,
            crab.Status == CrabStatus.Alive || crab.Status == CrabStatus.Molting || crab.Status == CrabStatus.Quarantined,
            crab.MoltedAt,
            crab.StockedAt,
            JsonStringList.Parse(crab.ImageUrlsJson)), "Updated.");
    }

    public async Task<ApiResponse<CrabBoxAllocationDto>> MoveCrabAsync(
        string sourceBoxQrCode, MoveCrabByScanRequest req, CancellationToken ct = default)
    {
        var sourceQr = await RequireBoxQrAsync(sourceBoxQrCode, ct);
        await _areas.EnsureBoxAsync(sourceQr.BoxId!.Value, manage: false, ct);
        var crab = await _uow.Crabs.GetByIdAsync(req.CrabId, ct)
            ?? throw AppException.NotFound("Crab");

        if (GetCrabBoxId(crab) != sourceQr.BoxId)
            throw AppException.BadRequest("Cua không đang ở hộp nguồn (QR vừa quét).");

        // Đích: BoxId hoặc quét thêm tem hộp đích
        Guid targetBoxId;
        if (req.TargetBoxId.HasValue)
        {
            targetBoxId = req.TargetBoxId.Value;
        }
        else if (!string.IsNullOrWhiteSpace(req.TargetQrCode))
        {
            var targetQr = await RequireBoxQrAsync(req.TargetQrCode!, ct);
            targetBoxId = targetQr.BoxId!.Value;
        }
        else
        {
            throw AppException.BadRequest("Cần TargetBoxId hoặc TargetQrCode của hộp đích.");
        }

        if (targetBoxId == sourceQr.BoxId)
            throw AppException.BadRequest("Hộp đích trùng hộp nguồn.");

        var targetBox = await _uow.Boxes.GetByIdAsync(targetBoxId, ct)
            ?? throw AppException.NotFound("Box");
        await _areas.EnsureBoxAsync(targetBoxId, manage: false, ct);
        var targetRow = await _uow.FarmingRows.GetByIdAsync(targetBox.FarmingRowId, ct)
            ?? throw AppException.NotFound("FarmingRow");
        if (crab.CrabLotId == Guid.Empty)
            throw AppException.BadRequest("Cua chưa được gắn Crab Lot.");
        _ = await CrabLotRegistrationGuard.EnsureLotAllowsBoxAsync(
            _uow, crab.CrabLotId, sourceQr.BoxId.Value, ct);
        _ = await CrabLotRegistrationGuard.EnsureLotAllowsBoxAsync(
            _uow, crab.CrabLotId, targetBoxId, ct);

        // Tái sử dụng logic allocation (đóng cũ / mở mới / cập nhật box)
        return await _history.AllocateCrabAsync(
            new AllocateCrabRequest(
                req.CrabId,
                targetBoxId,
                targetRow.FarmingAreaId,
                targetRow.Id,
                req.Notes ?? $"Move via QR {sourceBoxQrCode}"),
            ct);
    }

    private async Task<QrCode> RequireBoxQrAsync(string code, CancellationToken ct)
    {
        var qr = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.Code == code.Trim() && q.IsActive, ct)
            ?? throw AppException.NotFound("QrCode");
        if (qr.EntityType != "box" || qr.BoxId is null)
            throw AppException.BadRequest("QR không phải tem hộp nuôi.");
        return qr;
    }

    private static BoxQrDto MapQr(QrCode q) =>
        new(q.Id, q.Code, q.BoxId!.Value, q.Payload, q.ScanCount, q.IsActive);

    private static bool IsCrabAlive(Crab c) =>
    c.Status == CrabStatus.Alive
    || c.Status == CrabStatus.Molting
    || c.Status == CrabStatus.Quarantined;

    private static Guid GetCrabBoxId(Crab c) =>
        c.BoxAllocations
         .OrderByDescending(a => a.StartTime)
         .FirstOrDefault()?.BoxId
         ?? c.BoxId
         ?? Guid.Empty;
}

