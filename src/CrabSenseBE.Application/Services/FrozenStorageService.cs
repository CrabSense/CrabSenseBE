using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.FrozenStorage;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.SkiaSharp;

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


    public async Task<ApiResponse<IEnumerable<FrozenCrabItemDto>>> RegisterCrabItemsAsync(
    Guid lotId,
    RegisterFrozenCrabItemsRequest request,
    CancellationToken ct = default)
    {
        if (lotId == Guid.Empty)
            throw AppException.BadRequest("Frozen lot id is required.");

        if (request.Items is null || request.Items.Count == 0)
            throw AppException.BadRequest("At least one frozen crab item is required.");

        var lot = await _uow.FrozenLots.GetByIdAsync(lotId, ct)
            ?? throw AppException.NotFound("Frozen lot");

        if (lot.HarvestVoucherId is not Guid voucherId || voucherId == Guid.Empty)
            throw AppException.BadRequest(
                "The frozen lot must be linked to a harvest voucher before registering crab items.");

        var voucher = await _uow.HarvestVouchers.GetByIdAsync(voucherId, ct)
            ?? throw AppException.NotFound("Harvest voucher");

        // Request đăng ký toàn bộ sản phẩm cua của lô trong một lần.
        if (request.Items.Count != lot.Quantity)
            throw AppException.BadRequest(
                $"The number of crab items ({request.Items.Count}) must match the lot quantity ({lot.Quantity}).");

        var inputs = request.Items.ToList();

        if (inputs.Any(x => x.HarvestLineId == Guid.Empty))
            throw AppException.BadRequest("Every item must have a valid HarvestLineId.");

        var harvestLineIds = inputs
            .Select(x => x.HarvestLineId)
            .ToList();

        // Mỗi dòng thu hoạch chỉ được xuất hiện một lần trong request.
        if (harvestLineIds.Distinct().Count() != harvestLineIds.Count)
            throw AppException.BadRequest(
                "A HarvestLineId cannot be used more than once in the same request.");

        // Kiểm tra cân nặng riêng của từng con cua.
        if (inputs.Any(x => x.WeightGram <= 0))
            throw AppException.BadRequest("Every crab weight must be greater than zero.");

        // Chuẩn hóa size và chỉ chấp nhận S, M hoặc L.
        var normalizedGrades = inputs
            .Select(x => string.IsNullOrWhiteSpace(x.Grade)
                ? string.Empty
                : x.Grade.Trim().ToUpperInvariant())
            .ToList();

        if (normalizedGrades.Any(grade => !AllowedGrades.Contains(grade)))
            throw AppException.BadRequest("Crab size must be S, M, or L.");

        // Không cho đăng ký sản phẩm cua lần thứ hai cho cùng một lô.
        var existingItemsForLot = await _uow.FrozenCrabItems.FindAsync(
            x => x.FrozenLotId == lotId,
            ct);

        if (existingItemsForLot.Any())
            throw AppException.Conflict(
                "Crab items have already been registered for this frozen lot.");

        var requestedIds = harvestLineIds.ToHashSet();

        // Chỉ chấp nhận các dòng thu hoạch thuộc phiếu đã liên kết với lô này.
        var harvestLines = (await _uow.HarvestLines.FindAsync(
            line => line.HarvestVoucherId == voucherId
                    && requestedIds.Contains(line.Id),
            ct)).ToList();

        if (harvestLines.Count != inputs.Count)
            throw AppException.BadRequest(
                "Every HarvestLineId must belong to the harvest voucher linked to this frozen lot.");

        var linesById = harvestLines.ToDictionary(line => line.Id);

        // Đảm bảo mỗi dòng thu hoạch hợp lệ và thực sự tham chiếu đến một con cua.
        foreach (var input in inputs)
        {
            var line = linesById[input.HarvestLineId];

            if (!line.CrabId.HasValue || line.CrabId.Value == Guid.Empty)
                throw AppException.BadRequest(
                    $"Harvest line '{line.Id}' is not linked to a crab.");

            if (!string.Equals(line.Result, "passed", StringComparison.OrdinalIgnoreCase))
                throw AppException.BadRequest(
                    $"Harvest line '{line.Id}' is not a passed harvest item.");
        }

        // Không cho cùng một con cua được đăng ký nhiều lần qua các dòng khác nhau.
        var crabIds = inputs
            .Select(input => linesById[input.HarvestLineId].CrabId!.Value)
            .ToList();

        if (crabIds.Distinct().Count() != crabIds.Count)
            throw AppException.BadRequest(
                "A crab cannot be registered more than once in the same frozen lot.");

        // Không cho một dòng thu hoạch đã đăng ký ở lô khác được dùng lại.
        var registeredLines = await _uow.FrozenCrabItems.FindAsync(
            item => requestedIds.Contains(item.HarvestLineId),
            ct);

        if (registeredLines.Any())
            throw AppException.Conflict(
                "One or more harvest lines have already been registered as frozen crab items.");

        var createdItems = new List<FrozenCrabItem>(inputs.Count);

        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var line = linesById[input.HarvestLineId];
            var crabId = line.CrabId!.Value;

            // Mã barcode được lưu cố định để các lần tra cứu sau dùng cùng một mã.
            var item = new FrozenCrabItem
            {
                FrozenLotId = lot.Id,
                HarvestLineId = line.Id,
                CrabId = crabId,
                BarcodeValue = $"FC-{Guid.NewGuid():N}".ToUpperInvariant(),

                // Lưu snapshot mã lô, mã cua và mã phiếu tại thời điểm đóng gói.
                LotCode = lot.LotCode,
                CrabCode = string.IsNullOrWhiteSpace(line.CrabCode)
                    ? crabId.ToString()
                    : line.CrabCode.Trim(),
                HarvestVoucherCode = voucher.VoucherCode,

                // Lưu snapshot các mốc thời gian để tra cứu sản phẩm.
                HarvestDate = voucher.HarvestDate,
                FrozenDate = lot.FrozenDate,
                ExpiryDate = lot.ExpiryDate,

                // Đây là cân nặng và size của từng con, không phải tổng của cả lô.
                WeightGram = input.WeightGram,
                Grade = normalizedGrades[i]
            };

            createdItems.Add(item);
        }

        // Chỉ thêm dữ liệu sau khi đã kiểm tra hợp lệ toàn bộ request.
        foreach (var item in createdItems)
            await _uow.FrozenCrabItems.AddAsync(item, ct);

        // Lưu cả lô sản phẩm trong một lần.
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<IEnumerable<FrozenCrabItemDto>>.Ok(
            createdItems.Select(MapToFrozenCrabItemDto).ToList(),
            "Frozen crab items and barcodes registered.");
    }

    public async Task<ApiResponse<FrozenCrabItemDto>> GetCrabItemByBarcodeAsync(
        string barcodeValue,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(barcodeValue))
            throw AppException.BadRequest("Barcode value is required.");

        // Chuẩn hóa mã trước khi tìm kiếm.
        var normalizedBarcode = barcodeValue.Trim().ToUpperInvariant();

        var item = await _uow.FrozenCrabItems.FirstOrDefaultAsync(
            x => x.BarcodeValue == normalizedBarcode,
            ct)
            ?? throw AppException.NotFound("Frozen crab item");

        return ApiResponse<FrozenCrabItemDto>.Ok(
            MapToFrozenCrabItemDto(item));
    }

    public async Task<byte[]> GetCrabItemBarcodePngAsync(
    string barcodeValue,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(barcodeValue))
        throw AppException.BadRequest("Barcode value is required.");

    // Chuẩn hóa giá trị trước khi tìm barcode đã lưu.
    var normalizedBarcode = barcodeValue.Trim().ToUpperInvariant();

    var item = await _uow.FrozenCrabItems.FirstOrDefaultAsync(
        x => x.BarcodeValue == normalizedBarcode,
        ct)
        ?? throw AppException.NotFound("Frozen crab item");

    // Tạo ảnh Code 128; giá trị encode chính là mã đã lưu cho cua này.
    var writer = new BarcodeWriter
    {
        Format = BarcodeFormat.CODE_128,
        Options = new EncodingOptions
        {
            Width = 600,
            Height = 160,
            Margin = 20
        }
    };

    using var bitmap = writer.Write(item.BarcodeValue);
    using var image = SKImage.FromBitmap(bitmap);
    using var png = image.Encode(SKEncodedImageFormat.Png, quality: 100);

    return png.ToArray();
}

    // Chuyển entity sản phẩm cua cấp đông sang DTO trả về cho API.
    private static FrozenCrabItemDto MapToFrozenCrabItemDto(FrozenCrabItem item)
    {
        return new FrozenCrabItemDto(
            item.Id,
            item.BarcodeValue,
            item.LotCode,
            item.CrabCode,
            item.HarvestVoucherCode,
            item.HarvestDate,
            item.FrozenDate,
            item.ExpiryDate,
            item.WeightGram,
            item.Grade);
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
