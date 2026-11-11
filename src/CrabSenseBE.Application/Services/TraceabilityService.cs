using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using QRCoder;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Truy xuất nguồn gốc — QR cho FrozenLot, HarvestVoucher và endpoint công khai.
/// </summary>
public class TraceabilityService : ITraceabilityService
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService? _currentUser;
    private readonly FarmAreaScope _areas;

    public TraceabilityService(IUnitOfWork uow, ICurrentUserService? currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
        _areas = new FarmAreaScope(uow, currentUser);
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
        await EnsureFrozenLotAreaAsync(lot, ct);

        // Đã có QR active → trả về luôn
        var existing = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.FrozenLotId == frozenLotId
                 && q.IsActive
                 && q.EntityType == "frozen_lot",
            ct);

        if (existing is not null && existing.ApprovalStatus != QrApprovalStatus.Rejected)
            return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(existing));
        if (existing is not null)
        {
            existing.ApprovalStatus = QrApprovalStatus.Pending;
            existing.ProposedByUserId = CurrentUserId();
            existing.ApprovedByUserId = null;
            existing.ApprovedAt = null;
            existing.RejectionReason = null;
            _uow.QrCodes.Update(existing);
            await _uow.SaveChangesAsync(ct);
            return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(existing), "QR assignment proposed again.");
        }

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
            ApprovalStatus = QrApprovalStatus.Pending,
            ProposedByUserId = CurrentUserId(),
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
            throw AppException.NotFound("QrCode");
        var lot = await _uow.FrozenLots.GetByIdAsync(frozenLotId, ct)
            ?? throw AppException.NotFound("FrozenLot");
        await EnsureFrozenLotAreaAsync(lot, ct);

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
        await EnsureHarvestVoucherAreaAsync(voucher, ct);

        // Đã có QR active → trả về luôn
        var existing = await _uow.QrCodes.FirstOrDefaultAsync(
            q => q.HarvestVoucherId == voucherId
                 && q.IsActive
                 && q.EntityType == "harvest_voucher",
            ct);

        if (existing is not null && existing.ApprovalStatus != QrApprovalStatus.Rejected)
            return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(existing));
        if (existing is not null)
        {
            existing.ApprovalStatus = QrApprovalStatus.Pending;
            existing.ProposedByUserId = CurrentUserId();
            existing.ApprovedByUserId = null;
            existing.ApprovedAt = null;
            existing.RejectionReason = null;
            _uow.QrCodes.Update(existing);
            await _uow.SaveChangesAsync(ct);
            return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(existing), "QR assignment proposed again.");
        }

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
            ApprovalStatus = QrApprovalStatus.Pending,
            ProposedByUserId = CurrentUserId(),
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
            throw AppException.NotFound("QrCode");
        var voucher = await _uow.HarvestVouchers.GetByIdAsync(voucherId, ct)
            ?? throw AppException.NotFound("HarvestVoucher");
        await EnsureHarvestVoucherAreaAsync(voucher, ct);

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

    public async Task<ApiResponse<QrCodeDto>> ApproveQrAsync(
        Guid qrId,
        CancellationToken ct = default)
    {
        var qr = await GetProductQrWithAreaCheckAsync(qrId, ct);
        if (qr.ApprovalStatus != QrApprovalStatus.Pending)
            throw AppException.Conflict("Chỉ mã QR đang chờ duyệt mới được phê duyệt.");
        var currentUserId = CurrentUserId();
        if (currentUserId.HasValue && qr.ProposedByUserId == currentUserId)
            throw AppException.Forbidden("Người đề xuất không được tự phê duyệt QR sản phẩm.");
        qr.ApprovalStatus = QrApprovalStatus.Approved;
        qr.ApprovedByUserId = currentUserId;
        qr.ApprovedAt = DateTime.UtcNow;
        qr.RejectionReason = null;
        _uow.QrCodes.Update(qr);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(qr), "QR assignment approved.");
    }

    public async Task<ApiResponse<QrCodeDto>> RejectQrAsync(
        Guid qrId,
        RejectQrAssignmentRequest request,
        CancellationToken ct = default)
    {
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is null)
            throw AppException.BadRequest("Cần ghi lý do từ chối gán QR.");
        var qr = await GetProductQrWithAreaCheckAsync(qrId, ct);
        if (qr.ApprovalStatus != QrApprovalStatus.Pending)
            throw AppException.Conflict("Chỉ mã QR đang chờ duyệt mới được từ chối.");
        var currentUserId = CurrentUserId();
        if (currentUserId.HasValue && qr.ProposedByUserId == currentUserId)
            throw AppException.Forbidden("Người đề xuất không được tự từ chối QR sản phẩm.");
        qr.ApprovalStatus = QrApprovalStatus.Rejected;
        qr.ApprovedByUserId = currentUserId;
        qr.ApprovedAt = DateTime.UtcNow;
        qr.RejectionReason = reason;
        _uow.QrCodes.Update(qr);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<QrCodeDto>.Ok(MapQrCodeDto(qr), "QR assignment rejected.");
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
            q => q.Code == normalized
                 && q.IsActive
                 && q.ApprovalStatus == QrApprovalStatus.Approved,
            ct)
            ?? throw AppException.NotFound("QrCode");
        if (qr.ApprovalStatus != QrApprovalStatus.Approved
            || qr.EntityType == "box")
            throw AppException.NotFound("QrCode");

        // Tăng scan count
        qr.ScanCount += 1;
        _uow.QrCodes.Update(qr);

        TraceabilityPublicDto result = qr.EntityType switch
        {
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
        new(
            q.Id,
            q.Code,
            q.FrozenLotId ?? q.HarvestVoucherId ?? q.BoxId,
            q.ScanCount,
            q.ExpiresAt,
            q.ApprovalStatus.ToString(),
            q.ProposedByUserId,
            q.ApprovedByUserId,
            q.ApprovedAt,
            q.RejectionReason);

    private Guid? CurrentUserId() =>
        _currentUser is { IsAuthenticated: true } ? _currentUser.UserId : null;

    private async Task EnsureFrozenLotAreaAsync(FrozenLot lot, CancellationToken ct)
    {
        if (lot.FarmingAreaId is Guid directAreaId)
        {
            await _areas.EnsureAsync(directAreaId, manage: false, ct);
            return;
        }
        if (lot.HarvestVoucherId is Guid voucherId)
        {
            var voucher = await _uow.HarvestVouchers.GetByIdAsync(voucherId, ct)
                ?? throw AppException.Forbidden("Không xác định được khu của lô đông.");
            await EnsureHarvestVoucherAreaAsync(voucher, ct);
            return;
        }
        if (await _areas.CurrentUserAsync(ct) is not null)
            throw AppException.Forbidden("Lô đông chưa xác định khu sở hữu.");
    }

    private async Task EnsureHarvestVoucherAreaAsync(
        HarvestVoucher voucher, CancellationToken ct)
    {
        if (voucher.FarmingAreaId is not Guid areaId)
        {
            if (await _areas.CurrentUserAsync(ct) is not null)
                throw AppException.Forbidden("Phiếu thu hoạch chưa xác định khu.");
            return;
        }
        await _areas.EnsureAsync(areaId, manage: false, ct);
    }

    private async Task<QrCode> GetProductQrWithAreaCheckAsync(
        Guid qrId, CancellationToken ct)
    {
        var qr = await _uow.QrCodes.GetByIdAsync(qrId, ct)
            ?? throw AppException.NotFound("QrCode");
        if (qr.EntityType == "frozen_lot" && qr.FrozenLotId is Guid lotId)
        {
            var lot = await _uow.FrozenLots.GetByIdAsync(lotId, ct)
                ?? throw AppException.NotFound("FrozenLot");
            await EnsureFrozenLotAreaAsync(lot, ct);
            return qr;
        }
        if (qr.EntityType == "harvest_voucher" && qr.HarvestVoucherId is Guid voucherId)
        {
            var voucher = await _uow.HarvestVouchers.GetByIdAsync(voucherId, ct)
                ?? throw AppException.NotFound("HarvestVoucher");
            await EnsureHarvestVoucherAreaAsync(voucher, ct);
            return qr;
        }
        throw AppException.BadRequest("Chỉ QR sản phẩm hoặc lô đông mới cần phê duyệt.");
    }

    private static byte[] GeneratePng(string code, int pixelsPerModule)
    {
        var ppm = Math.Clamp(pixelsPerModule, 4, 20);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(ppm, drawQuietZones: true);
    }
}
