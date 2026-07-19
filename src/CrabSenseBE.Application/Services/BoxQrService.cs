using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using QRCoder;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Nghiệp vụ QR hộp tại trại.
/// Luồng mobile: quét tem → ScanAsync → xem/sửa cua → MoveCrabAsync khi đổi hộp.
/// </summary>
public class BoxQrService : IBoxQrService
{
    private readonly IUnitOfWork _uow;
    private readonly IFarmHistoryService _history;

    public BoxQrService(IUnitOfWork uow, IFarmHistoryService history)
    {
        _uow = uow;
        _history = history;
    }

    public async Task<ApiResponse<BoxQrDto>> EnsureBoxQrAsync(Guid boxId, CancellationToken ct = default)
    {
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");

        // Đã có QR active cho box → trả về luôn
        var existing = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.BoxId == boxId && q.IsActive && q.EntityType == "box", ct);
        if (existing is not null)
            return ApiResponse<BoxQrDto>.Ok(MapQr(existing));

        // Mã tem: {boxCode}-{shortGuid} — unique, dễ in sticker
        var shortId = boxId.ToString("N")[..8].ToUpperInvariant();
        var code = $"{box.Code}-{shortId}";

        if (await _uow.QrCodes.AnyAsync(q => q.Code == code, ct))
            code = $"BOX-{shortId}-{Random.Shared.Next(100, 999)}";

        var qr = new QrCode
        {
            Code = code,
            EntityType = "box",
            BoxId = boxId,
            IsActive = true,
            // Deep-link gợi ý cho App (FE/App tự map scheme)
            Payload = JsonSerializer.Serialize(new
            {
                type = "box",
                boxId,
                boxCode = box.Code,
                path = $"/scan/box/{code}"
            })
        };
        await _uow.QrCodes.AddAsync(qr, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<BoxQrDto>.Ok(MapQr(qr), "QR created.");
    }

    public async Task<ApiResponse<BoxQrDto>> GetQrByBoxAsync(Guid boxId, CancellationToken ct = default)
    {
        var qr = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.BoxId == boxId && q.IsActive && q.EntityType == "box", ct);
        if (qr is null)
            return await EnsureBoxQrAsync(boxId, ct);
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
        var qr = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.Code == normalized && q.IsActive, ct)
            ?? throw AppException.NotFound("QrCode");

        if (qr.EntityType != "box" || qr.BoxId is null)
            throw AppException.BadRequest("QR này không gắn với hộp nuôi.");

        var box = await _uow.Boxes.GetByIdAsync(qr.BoxId.Value, ct)
            ?? throw AppException.NotFound("Box");

        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
        var areaName = "";
        var rowName = row?.Name ?? "";
        if (row is not null)
        {
            var area = await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);
            areaName = area?.Name ?? "";
        }

        // Cua đang trong hộp
        var crabs = (await _uow.Crabs.FindAsync(c => c.BoxId == box.Id && c.IsAlive, ct)).ToList();

        // Thời điểm bắt đầu allocation hiện tại (EndTime == null)
        var openAllocs = await _uow.CrabBoxAllocations.FindAsync(
            a => a.BoxId == box.Id && a.EndTime == null, ct);
        var allocByCrab = openAllocs
            .GroupBy(a => a.CrabId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartTime).First().StartTime);

        var crabDtos = crabs.Select(c => new BoxScanCrabDto(
            c.Id, c.Tag, c.WeightGram, c.MoltingStage, c.IsAlive, c.MoltedAt, c.CrabLotId, c.CropBatchId,
            allocByCrab.TryGetValue(c.Id, out var start) ? start : null
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
        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct)
            ?? throw AppException.NotFound("Crab");

        // Chỉ cho sửa cua đang thuộc hộp của tem vừa quét (tránh nhầm hộp)
        if (crab.BoxId != qr.BoxId)
            throw AppException.BadRequest("Cua không thuộc hộp của mã QR này.");

        if (req.Tag is not null) crab.Tag = req.Tag;
        if (req.WeightGram.HasValue) crab.WeightGram = req.WeightGram;
        if (req.MoltingStage is not null) crab.MoltingStage = req.MoltingStage;
        if (req.IsAlive.HasValue) crab.IsAlive = req.IsAlive.Value;
        if (req.MoltedAt.HasValue) crab.MoltedAt = req.MoltedAt;

        _uow.Crabs.Update(crab);
        await _uow.SaveChangesAsync(ct);

        var box = await _uow.Boxes.GetByIdAsync(crab.BoxId, ct);
        var row = box is null ? null : await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);

        return ApiResponse<CrabDto>.Ok(new CrabDto(
            crab.Id,
            crab.BoxId,
            box?.Code,
            box?.FarmingRowId ?? Guid.Empty,
            row?.FarmingAreaId ?? Guid.Empty,
            crab.CrabLotId,
            crab.CropBatchId,
            crab.Tag,
            crab.WeightGram,
            crab.MoltingStage,
            crab.IsAlive,
            crab.MoltedAt), "Updated.");
    }

    public async Task<ApiResponse<CrabBoxAllocationDto>> MoveCrabAsync(
        string sourceBoxQrCode, MoveCrabByScanRequest req, CancellationToken ct = default)
    {
        var sourceQr = await RequireBoxQrAsync(sourceBoxQrCode, ct);
        var crab = await _uow.Crabs.GetByIdAsync(req.CrabId, ct)
            ?? throw AppException.NotFound("Crab");

        if (crab.BoxId != sourceQr.BoxId)
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
        var targetRow = await _uow.FarmingRows.GetByIdAsync(targetBox.FarmingRowId, ct)
            ?? throw AppException.NotFound("FarmingRow");

        // Tái sử dụng logic allocation (đóng cũ / mở mới / cập nhật box)
        return await _history.AllocateCrabAsync(
            new AllocateCrabRequest(
                targetRow.FarmingAreaId,
                targetRow.Id,
                req.CrabId,
                targetBoxId,
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
}
