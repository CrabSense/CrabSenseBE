using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.FrozenStorage;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class FrozenStorageService : IFrozenStorageService
{
    private static readonly HashSet<string> AllowedGrades =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "S",
            "M",
            "L"
        };

    private readonly IUnitOfWork _uow;

    public FrozenStorageService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<ApiResponse<IEnumerable<FrozenLotDto>>> GetAllAsync(
        CancellationToken ct = default)
    {
        var lots = await _uow.FrozenLots.GetAllAsync(ct);

        var result = lots
            .OrderByDescending(x => x.FrozenDate)
            .Select(MapToDto)
            .ToList();

        return ApiResponse<IEnumerable<FrozenLotDto>>.Ok(
            result,
            "Lấy danh sách lô cấp đông thành công.");
    }

    public async Task<ApiResponse<FrozenLotDto>> GetByIdAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var lot = await _uow.FrozenLots.GetByIdAsync(id, ct);

        if (lot is null)
        {
            throw AppException.NotFound("Frozen lot");
        }

        return ApiResponse<FrozenLotDto>.Ok(
            MapToDto(lot),
            "Lấy thông tin lô cấp đông thành công.");
    }

    public async Task<ApiResponse<FrozenLotDto>> CreateAsync(
        CreateFrozenLotRequest request,
        CancellationToken ct = default)
    {
        ValidateCreateRequest(request);

        var normalizedLotCode = NormalizeLotCode(request.LotCode);
        var normalizedGrade = NormalizeGrade(request.Grade);
        var normalizedLocation = NormalizeOptionalText(request.StorageLocation);

        var duplicatedCode = await _uow.FrozenLots.AnyAsync(
            x => x.LotCode == normalizedLotCode,
            ct);

        if (duplicatedCode)
        {
            throw AppException.Conflict(
                $"Mã lô cấp đông '{normalizedLotCode}' đã tồn tại.");
        }

        if (request.HarvestVoucherId.HasValue)
        {
            var harvestVoucher = await _uow.HarvestVouchers.GetByIdAsync(
                request.HarvestVoucherId.Value,
                ct);

            if (harvestVoucher is null)
            {
                throw AppException.BadRequest(
                    "Phiếu thu hoạch được chọn không tồn tại.");
            }

            if (request.FrozenDate.Date < harvestVoucher.HarvestDate.Date)
            {
                throw AppException.BadRequest(
                    "Ngày cấp đông không được trước ngày thu hoạch.");
            }
        }

        var lot = new FrozenLot
        {
            LotCode = normalizedLotCode,
            HarvestVoucherId = request.HarvestVoucherId,
            FrozenDate = request.FrozenDate,
            ExpiryDate = request.ExpiryDate,
            WeightKg = request.WeightKg,
            Grade = normalizedGrade,
            Quantity = request.Quantity,
            Status = FrozenLotStatus.Available,
            StorageLocation = normalizedLocation
        };

        await _uow.FrozenLots.AddAsync(lot, ct);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<FrozenLotDto>.Ok(
            MapToDto(lot),
            "Tạo lô cấp đông thành công.");
    }

    public async Task<ApiResponse<FrozenLotDto>> UpdateAsync(
        Guid id,
        UpdateFrozenLotRequest request,
        CancellationToken ct = default)
    {
        ValidateUpdateRequest(request);

        var lot = await _uow.FrozenLots.GetByIdAsync(id, ct);

        if (lot is null)
        {
            throw AppException.NotFound("Frozen lot");
        }

        if (!Enum.TryParse<FrozenLotStatus>(
                request.Status,
                ignoreCase: true,
                out var status))
        {
            throw AppException.BadRequest(
                $"Trạng thái lô cấp đông '{request.Status}' không hợp lệ.");
        }

        if (lot.HarvestVoucherId.HasValue)
        {
            var harvestVoucher = await _uow.HarvestVouchers.GetByIdAsync(
                lot.HarvestVoucherId.Value,
                ct);

            if (harvestVoucher is not null &&
                request.FrozenDate.Date < harvestVoucher.HarvestDate.Date)
            {
                throw AppException.BadRequest(
                    "Ngày cấp đông không được trước ngày thu hoạch.");
            }
        }

        lot.FrozenDate = request.FrozenDate;
        lot.ExpiryDate = request.ExpiryDate;
        lot.WeightKg = request.WeightKg;
        lot.Grade = NormalizeGrade(request.Grade);
        lot.Quantity = request.Quantity;
        lot.Status = status;
        lot.StorageLocation = NormalizeOptionalText(request.StorageLocation);

        _uow.FrozenLots.Update(lot);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<FrozenLotDto>.Ok(
            MapToDto(lot),
            "Cập nhật lô cấp đông thành công.");
    }

    public async Task<ApiResponse> DeleteAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var lot = await _uow.FrozenLots.GetByIdAsync(id, ct);

        if (lot is null)
        {
            throw AppException.NotFound("Frozen lot");
        }

        if (lot.Status == FrozenLotStatus.Reserved)
        {
            throw AppException.Conflict(
                "Không thể xóa lô cấp đông đang được đặt trước.");
        }

        if (lot.Status == FrozenLotStatus.Shipped)
        {
            throw AppException.Conflict(
                "Không thể xóa lô cấp đông đã xuất kho.");
        }

        _uow.FrozenLots.Remove(lot);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse.Ok("Xóa lô cấp đông thành công.");
    }

    public async Task<ApiResponse<FrozenInventorySummaryDto>>
    GetInventorySummaryAsync(CancellationToken ct = default)
    {
    var allLots = await _uow.FrozenLots.GetAllAsync(ct);
    var today = DateTime.UtcNow.Date;

    /*
     * Shipped không còn nằm trong kho nên không được tính vào tồn kho.
     *
     * Một lô đã quá ExpiryDate được xem là Expired,
     * ngay cả khi Status trong database vẫn là Available hoặc Reserved.
     */
    var inventoryLots = allLots
        .Where(x => x.Status != FrozenLotStatus.Shipped)
        .Select(x => new FrozenInventoryItem(
            x,
            GetEffectiveStatus(x, today)))
        .ToList();

    var availableLots = inventoryLots
        .Where(x => x.Status == FrozenLotStatus.Available)
        .ToList();

    var reservedLots = inventoryLots
        .Where(x => x.Status == FrozenLotStatus.Reserved)
        .ToList();

    var expiredLots = inventoryLots
        .Where(x => x.Status == FrozenLotStatus.Expired)
        .ToList();

    var byGrade = inventoryLots
        .GroupBy(x => NormalizeGroupValue(x.Lot.Grade, "Unclassified"))
        .Select(group => new FrozenInventoryByGradeDto(
            Grade: group.Key,
            TotalLots: group.Count(),
            TotalQuantity: group.Sum(x => x.Lot.Quantity),
            TotalWeightKg: group.Sum(x => x.Lot.WeightKg)))
        .OrderBy(x => x.Grade)
        .ToList();

    var byLocation = inventoryLots
        .GroupBy(x => NormalizeGroupValue(
            x.Lot.StorageLocation,
            "Unassigned"))
        .Select(group => new FrozenInventoryByLocationDto(
            StorageLocation: group.Key,
            TotalLots: group.Count(),
            TotalQuantity: group.Sum(x => x.Lot.Quantity),
            TotalWeightKg: group.Sum(x => x.Lot.WeightKg)))
        .OrderBy(x => x.StorageLocation)
        .ToList();

    var byStatus = inventoryLots
        .GroupBy(x => x.Status)
        .Select(group => new FrozenInventoryByStatusDto(
            Status: group.Key.ToString(),
            TotalLots: group.Count(),
            TotalQuantity: group.Sum(x => x.Lot.Quantity),
            TotalWeightKg: group.Sum(x => x.Lot.WeightKg)))
        .OrderBy(x => x.Status)
        .ToList();

    var summary = new FrozenInventorySummaryDto(
        TotalLots: inventoryLots.Count,
        TotalQuantity: inventoryLots.Sum(x => x.Lot.Quantity),
        TotalWeightKg: inventoryLots.Sum(x => x.Lot.WeightKg),

        AvailableLots: availableLots.Count,
        AvailableQuantity: availableLots.Sum(x => x.Lot.Quantity),
        AvailableWeightKg: availableLots.Sum(x => x.Lot.WeightKg),

        ReservedLots: reservedLots.Count,
        ReservedQuantity: reservedLots.Sum(x => x.Lot.Quantity),
        ReservedWeightKg: reservedLots.Sum(x => x.Lot.WeightKg),

        ExpiredLots: expiredLots.Count,
        ExpiredQuantity: expiredLots.Sum(x => x.Lot.Quantity),
        ExpiredWeightKg: expiredLots.Sum(x => x.Lot.WeightKg),

        ByGrade: byGrade,
        ByLocation: byLocation,
        ByStatus: byStatus);

    return ApiResponse<FrozenInventorySummaryDto>.Ok(
        summary,
        "Lấy thống kê tồn kho cấp đông thành công.");
    }


    public async Task<ApiResponse<IEnumerable<FrozenStorageAgingDto>>>
    GetStorageAgingAsync(CancellationToken ct = default)
    {
        var lots = await _uow.FrozenLots.GetAllAsync(ct);
        var today = DateTime.UtcNow.Date;

        var result = lots
            .Where(x => x.Status != FrozenLotStatus.Shipped)
            .Select(x =>
            {
                var storageDays = Math.Max(
                    0,
                    (today - x.FrozenDate.Date).Days);

                var remainingDays =
                    (x.ExpiryDate.Date - today).Days;

                var agingStatus = GetAgingStatus(remainingDays);

                return new FrozenStorageAgingDto(
                    Id: x.Id,
                    LotCode: x.LotCode,
                    FrozenDate: x.FrozenDate,
                    ExpiryDate: x.ExpiryDate,
                    StorageDays: storageDays,
                    RemainingDays: remainingDays,
                    AgingStatus: agingStatus,
                    Grade: x.Grade,
                    Quantity: x.Quantity,
                    WeightKg: x.WeightKg,
                    StorageLocation: x.StorageLocation);
            })
            .OrderBy(x => x.RemainingDays)
            .ThenBy(x => x.LotCode)
            .ToList();

        return ApiResponse<IEnumerable<FrozenStorageAgingDto>>.Ok(
            result,
            "Lấy thông tin thời gian lưu kho thành công.");
    }

    public async Task<ApiResponse<IEnumerable<ExpiringFrozenLotDto>>>
    GetExpiringLotsAsync(
        int days,
        CancellationToken ct = default)
    {
        if (days <= 0)
        {
            throw AppException.BadRequest(
                "Số ngày cảnh báo phải lớn hơn 0.");
        }

        if (days > 365)
        {
            throw AppException.BadRequest(
                "Số ngày cảnh báo không được lớn hơn 365.");
        }

        var lots = await _uow.FrozenLots.GetAllAsync(ct);
        var today = DateTime.UtcNow.Date;

        var result = lots
            .Where(x => x.Status != FrozenLotStatus.Shipped)
            .Select(x =>
            {
                var storageDays = Math.Max(
                    0,
                    (today - x.FrozenDate.Date).Days);

                var remainingDays =
                    (x.ExpiryDate.Date - today).Days;

                return new
                {
                    Lot = x,
                    StorageDays = storageDays,
                    RemainingDays = remainingDays
                };
            })
            .Where(x =>
                x.RemainingDays >= 0 &&
                x.RemainingDays <= days)
            .Select(x => new ExpiringFrozenLotDto(
                Id: x.Lot.Id,
                LotCode: x.Lot.LotCode,
                FrozenDate: x.Lot.FrozenDate,
                ExpiryDate: x.Lot.ExpiryDate,
                StorageDays: x.StorageDays,
                RemainingDays: x.RemainingDays,
                AlertLevel: GetExpiryAlertLevel(
                    x.RemainingDays,
                    days),
                Status: x.Lot.Status.ToString(),
                Grade: x.Lot.Grade,
                Quantity: x.Lot.Quantity,
                WeightKg: x.Lot.WeightKg,
                StorageLocation: x.Lot.StorageLocation))
            .OrderBy(x => x.RemainingDays)
            .ThenBy(x => x.LotCode)
            .ToList();

        return ApiResponse<IEnumerable<ExpiringFrozenLotDto>>.Ok(
            result,
            $"Lấy danh sách lô cấp đông sắp hết hạn trong {days} ngày thành công.");
    }

    //helper//

    private static void ValidateCreateRequest(CreateFrozenLotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LotCode))
        {
            throw AppException.BadRequest(
                "Mã lô cấp đông không được để trống.");
        }

        ValidateCommonFields(
            request.FrozenDate,
            request.ExpiryDate,
            request.WeightKg,
            request.Quantity,
            request.Grade);
    }

    private static void ValidateUpdateRequest(UpdateFrozenLotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            throw AppException.BadRequest(
                "Trạng thái lô cấp đông không được để trống.");
        }

        ValidateCommonFields(
            request.FrozenDate,
            request.ExpiryDate,
            request.WeightKg,
            request.Quantity,
            request.Grade);
    }

    private static void ValidateCommonFields(
        DateTime frozenDate,
        DateTime expiryDate,
        decimal weightKg,
        int quantity,
        string? grade)
    {
        if (frozenDate == default)
        {
            throw AppException.BadRequest(
                "Ngày cấp đông không hợp lệ.");
        }

        if (expiryDate == default)
        {
            throw AppException.BadRequest(
                "Ngày hết hạn không hợp lệ.");
        }

        if (expiryDate <= frozenDate)
        {
            throw AppException.BadRequest(
                "Ngày hết hạn phải sau ngày cấp đông.");
        }

        if (weightKg <= 0)
        {
            throw AppException.BadRequest(
                "Trọng lượng lô phải lớn hơn 0.");
        }

        if (quantity <= 0)
        {
            throw AppException.BadRequest(
                "Số lượng cua trong lô phải lớn hơn 0.");
        }

        if (!string.IsNullOrWhiteSpace(grade) &&
            !AllowedGrades.Contains(grade.Trim()))
        {
            throw AppException.BadRequest(
                "Phân loại lô chỉ chấp nhận S, M hoặc L.");
        }
    }

    private static string NormalizeLotCode(string lotCode)
    {
        return lotCode.Trim().ToUpperInvariant();
    }

    private static string? NormalizeGrade(string? grade)
    {
        return string.IsNullOrWhiteSpace(grade)
            ? null
            : grade.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static FrozenLotDto MapToDto(FrozenLot lot)
    {
        var effectiveStatus = GetEffectiveStatus(
            lot,
            DateTime.UtcNow.Date);

        return new FrozenLotDto(
            lot.Id,
            lot.LotCode,
            lot.HarvestVoucherId,
            lot.FrozenDate,
            lot.ExpiryDate,
            lot.WeightKg,
            lot.Grade,
            lot.Quantity,
            effectiveStatus.ToString(),
            lot.StorageLocation,
            lot.CreatedAt,
            lot.UpdatedAt);
    }


    private static FrozenLotStatus GetEffectiveStatus(
    FrozenLot lot,
    DateTime today)
    {
    if (lot.Status == FrozenLotStatus.Shipped)
    {
            return FrozenLotStatus.Shipped;
    }

    if (lot.ExpiryDate.Date < today)
    {
        return FrozenLotStatus.Expired;
    }

        return lot.Status;
    }

    private static string NormalizeGroupValue(
        string? value,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private sealed record FrozenInventoryItem(
        FrozenLot Lot,
        FrozenLotStatus Status);

    private static string GetAgingStatus(int remainingDays)
    {
        if (remainingDays < 0)
        {
            return "Expired";
        }

        if (remainingDays <= 30)
        {
            return "Warning";
        }

        return "Healthy";
    }

    private static string GetExpiryAlertLevel(
        int remainingDays,
        int warningDays)
    {
        if (remainingDays == 0)
        {
            return "Critical";
        }

        var criticalThreshold = Math.Max(
            1,
            Math.Min(7, warningDays / 3));

        if (remainingDays <= criticalThreshold)
        {
            return "Critical";
        }

        return "Warning";
    }

}
