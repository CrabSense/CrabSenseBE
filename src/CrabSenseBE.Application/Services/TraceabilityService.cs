using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using QRCoder;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Truy xuất nguồn gốc — QR cho FrozenLot, HarvestVoucher và endpoint công khai.
/// </summary>
public class TraceabilityService : ITraceabilityService
{
    private readonly IUnitOfWork _uow;

    public TraceabilityService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FrozenLot QR
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<QrCodeDto>> EnsureFrozenLotQrAsync(
        Guid frozenLotId,
        CancellationToken ct = default)
    {
        var lot = await _uow.FrozenLots.GetByIdAsync(frozenLotId, ct)
            ?? throw AppException.NotFound("FrozenLot");

        // Đã có QR active → trả về luôn
        var existing = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.FrozenLotId == frozenLotId
                 && q.IsActive
                 && q.EntityType == "frozen_lot",
            ct);

        if (existing is not null)
            return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(existing));

        // Mã tem: FL-{lotCode}-{shortGuid}
        var shortId = frozenLotId.ToString("N")[..8].ToUpperInvariant();
        var code = $"FL-{lot.LotCode}-{shortId}";

        if (await _uow.QrCodes.AnyAsync(q => q.Code == code, ct))
            code = $"FL-{shortId}-{Random.Shared.Next(100, 999)}";

        var qr = new QrCode
        {
            Code = code,
            EntityType = "frozen_lot",
            FrozenLotId = frozenLotId,
            IsActive = true,
            Payload = JsonSerializer.Serialize(new
            {
                type = "frozen_lot",
                frozenLotId,
                lotCode = lot.LotCode,
                path = $"/traceability/{code}"
            })
        };

        await _uow.QrCodes.AddAsync(qr, ct);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(qr), "QR created.");
    }

    public async Task<ApiResponse<QrCodeDto>> GetFrozenLotQrAsync(
        Guid frozenLotId,
        CancellationToken ct = default)
    {
        var qr = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.FrozenLotId == frozenLotId
                 && q.IsActive
                 && q.EntityType == "frozen_lot",
            ct);

        if (qr is null)
            return await EnsureFrozenLotQrAsync(frozenLotId, ct);

        return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(qr));
    }

    public async Task<byte[]> GetFrozenLotQrPngAsync(
        Guid frozenLotId,
        int pixelsPerModule = 8,
        CancellationToken ct = default)
    {
        var result = await GetFrozenLotQrAsync(frozenLotId, ct);
        var code = result.Data?.Code
            ?? throw AppException.NotFound("QrCode");

        return GeneratePng(code, pixelsPerModule);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HarvestVoucher QR
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<QrCodeDto>> EnsureHarvestVoucherQrAsync(
        Guid voucherId,
        CancellationToken ct = default)
    {
        var voucher = await _uow.HarvestVouchers.GetByIdAsync(voucherId, ct)
            ?? throw AppException.NotFound("HarvestVoucher");

        // Đã có QR active → trả về luôn
        var existing = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.HarvestVoucherId == voucherId
                 && q.IsActive
                 && q.EntityType == "harvest_voucher",
            ct);

        if (existing is not null)
            return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(existing));

        // Mã tem: HV-{voucherCode}-{shortGuid}
        var shortId = voucherId.ToString("N")[..8].ToUpperInvariant();
        var code = $"HV-{voucher.VoucherCode}-{shortId}";

        if (await _uow.QrCodes.AnyAsync(q => q.Code == code, ct))
            code = $"HV-{shortId}-{Random.Shared.Next(100, 999)}";

        var qr = new QrCode
        {
            Code = code,
            EntityType = "harvest_voucher",
            HarvestVoucherId = voucherId,
            IsActive = true,
            Payload = JsonSerializer.Serialize(new
            {
                type = "harvest_voucher",
                voucherId,
                voucherCode = voucher.VoucherCode,
                path = $"/traceability/{code}"
            })
        };

        await _uow.QrCodes.AddAsync(qr, ct);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(qr), "QR created.");
    }

    public async Task<ApiResponse<QrCodeDto>> GetHarvestVoucherQrAsync(
        Guid voucherId,
        CancellationToken ct = default)
    {
        var qr = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.HarvestVoucherId == voucherId
                 && q.IsActive
                 && q.EntityType == "harvest_voucher",
            ct);

        if (qr is null)
            return await EnsureHarvestVoucherQrAsync(voucherId, ct);

        return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(qr));
    }

    public async Task<byte[]> GetHarvestVoucherQrPngAsync(
        Guid voucherId,
        int pixelsPerModule = 8,
        CancellationToken ct = default)
    {
        var result = await GetHarvestVoucherQrAsync(voucherId, ct);
        var code = result.Data?.Code
            ?? throw AppException.NotFound("QrCode");

        return GeneratePng(code, pixelsPerModule);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Traceability (public)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<TraceabilityPublicDto>> GetTraceabilityAsync(
        string code,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw AppException.BadRequest("QR code is required.");

        var normalized = code.Trim();

        var qr = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.Code == normalized && q.IsActive,
            ct)
            ?? throw AppException.NotFound("QrCode");

        // Tăng scan count
        qr.ScanCount += 1;
        _uow.QrCodes.Update(qr);

        TraceabilityPublicDto result = qr.EntityType switch
        {
            "box" => await BuildBoxTraceabilityAsync(qr, ct),
            "frozen_lot" => await BuildFrozenLotTraceabilityAsync(qr, ct),
            "harvest_voucher" => await BuildHarvestVoucherTraceabilityAsync(qr, ct),
            _ => throw AppException.BadRequest($"Unsupported entity type: {qr.EntityType}")
        };

        await _uow.SaveChangesAsync(ct);

        return ApiResponse<TraceabilityPublicDto>.Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers — Build traceability per entity type
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<TraceabilityPublicDto> BuildBoxTraceabilityAsync(
        QrCode qr,
        CancellationToken ct)
    {
        // QR gắn với box → lấy box + tìm harvest voucher gần nhất
        var boxId = qr.BoxId ?? throw AppException.BadRequest("QR does not link to a box.");

        var box = await _uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");

        // Tìm harvest voucher thông qua HarvestLine
        var harvestLine = await _uow.HarvestLines.FirstOrDefaultAsync(
            l => l.BoxId == boxId,
            ct);

        string? frozenLotCode = null;
        string? voucherCode = null;
        DateTime? harvestDate = null;
        string? grade = null;

        if (harvestLine is not null)
        {
            var voucher = await _uow.HarvestVouchers.GetByIdAsync(
                harvestLine.HarvestVoucherId, ct);

            voucherCode = voucher?.VoucherCode;
            harvestDate = voucher?.HarvestDate;
            grade = harvestLine.Grade;

            // Tìm frozen lot tham chiếu
            if (voucher is not null)
            {
                var frozenLot = await _uow.FrozenLots.FirstOrDefaultAsync(
                    l => l.HarvestVoucherId == voucher.Id,
                    ct);
                frozenLotCode = frozenLot?.LotCode;
            }
        }

        return new TraceabilityPublicDto(
            Code: qr.Code,
            FrozenLotCode: frozenLotCode,
            HarvestVoucherCode: voucherCode,
            FrozenDate: null,
            HarvestDate: harvestDate,
            Grade: grade);
    }

    private async Task<TraceabilityPublicDto> BuildFrozenLotTraceabilityAsync(
        QrCode qr,
        CancellationToken ct)
    {
        var frozenLotId = qr.FrozenLotId
            ?? throw AppException.BadRequest("QR does not link to a frozen lot.");

        var lot = await _uow.FrozenLots.GetByIdAsync(frozenLotId, ct)
            ?? throw AppException.NotFound("FrozenLot");

        string? voucherCode = null;
        DateTime? harvestDate = null;

        if (lot.HarvestVoucherId.HasValue)
        {
            var voucher = await _uow.HarvestVouchers.GetByIdAsync(
                lot.HarvestVoucherId.Value, ct);
            voucherCode = voucher?.VoucherCode;
            harvestDate = voucher?.HarvestDate;
        }

        return new TraceabilityPublicDto(
            Code: qr.Code,
            FrozenLotCode: lot.LotCode,
            HarvestVoucherCode: voucherCode,
            FrozenDate: lot.FrozenDate,
            HarvestDate: harvestDate,
            Grade: lot.Grade);
    }

    private async Task<TraceabilityPublicDto> BuildHarvestVoucherTraceabilityAsync(
        QrCode qr,
        CancellationToken ct)
    {
        var voucherId = qr.HarvestVoucherId
            ?? throw AppException.BadRequest("QR does not link to a harvest voucher.");

        var voucher = await _uow.HarvestVouchers.GetByIdAsync(voucherId, ct)
            ?? throw AppException.NotFound("HarvestVoucher");

        // Tìm frozen lot tham chiếu
        var frozenLot = await _uow.FrozenLots.FirstOrDefaultAsync(
            l => l.HarvestVoucherId == voucherId,
            ct);

        // Lấy grade từ dòng thu hoạch đầu tiên
        var firstLine = await _uow.HarvestLines.FirstOrDefaultAsync(
            l => l.HarvestVoucherId == voucherId,
            ct);

        return new TraceabilityPublicDto(
            Code: qr.Code,
            FrozenLotCode: frozenLot?.LotCode,
            HarvestVoucherCode: voucher.VoucherCode,
            FrozenDate: frozenLot?.FrozenDate,
            HarvestDate: voucher.HarvestDate,
            Grade: firstLine?.Grade);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mapping & Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static QrCodeDto MapQrCodeDto(QrCode q) =>
        new(q.Id, q.Code, q.FrozenLotId ?? q.HarvestVoucherId ?? q.BoxId, q.ScanCount, q.ExpiresAt);

    private static byte[] GeneratePng(string code, int pixelsPerModule)
    {
        var ppm = Math.Clamp(pixelsPerModule, 4, 20);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(ppm, drawQuietZones: true);
    }
}
