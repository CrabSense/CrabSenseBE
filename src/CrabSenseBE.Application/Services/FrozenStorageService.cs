using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.FrozenStorage;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
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
    private readonly ICurrentUserService _currentUser;
    private readonly FarmAreaScope _areas;

    public FrozenStorageService(IUnitOfWork uow, ICurrentUserService currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public async Task<ApiResponse<IEnumerable<FrozenLotDto>>> GetAllAsync(
        CancellationToken ct = default)
    {
        var allowedAreas = await _areas.AreaIdsAsync(ct);
        var lots = (await _uow.FrozenLots.GetAllAsync(ct))
            .Where(lot => allowedAreas is null
                || (lot.FarmingAreaId is Guid areaId && allowedAreas.Contains(areaId)));

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
        await EnsureLotAreaAsync(lot, ct);

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

        Guid? voucherAreaId = null;
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
            if (harvestVoucher.Status != HarvestStatus.Completed)
                throw AppException.Conflict(
                    "Chỉ được tạo lô đông từ phiếu thu hoạch đã được xác nhận hoàn tất.");

            if (request.FrozenDate.Date < harvestVoucher.HarvestDate.Date)
            {
                throw AppException.BadRequest(
                    "Ngày cấp đông không được trước ngày thu hoạch.");
            }
            voucherAreaId = harvestVoucher.FarmingAreaId;
            if (voucherAreaId is null || voucherAreaId == Guid.Empty)
                throw AppException.Conflict("Phiếu thu hoạch chưa xác định khu sở hữu; cần đối soát trước khi cấp đông.");
        }

        var areaId = request.FarmingAreaId ?? voucherAreaId;
        if (areaId is not Guid ownerAreaId || ownerAreaId == Guid.Empty)
            throw AppException.BadRequest("Lô đông phải xác định khu sở hữu.");
        if (voucherAreaId is Guid harvestAreaId && harvestAreaId != ownerAreaId)
            throw AppException.BadRequest("Lô đông và phiếu thu hoạch phải thuộc cùng một khu.");
        await _areas.EnsureAsync(ownerAreaId, manage: false, ct);

        var lot = new FrozenLot
        {
            LotCode = normalizedLotCode,
            HarvestVoucherId = request.HarvestVoucherId,
            FarmingAreaId = ownerAreaId,
            FrozenDate = request.FrozenDate,
            ExpiryDate = request.ExpiryDate,
            WeightKg = 0,
            Grade = normalizedGrade,
            Quantity = 0,
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
        await EnsureLotAreaAsync(lot, ct);

        if (!Enum.TryParse<FrozenLotStatus>(
                request.Status,
                ignoreCase: true,
                out var requestedStatus) || !Enum.IsDefined(requestedStatus))
        {
            throw AppException.BadRequest(
                $"Trạng thái lô cấp đông '{request.Status}' không hợp lệ.");
        }
        if (request.Quantity != lot.Quantity
            || request.WeightKg != lot.WeightKg
            || requestedStatus != lot.Status)
            throw AppException.BadRequest(
                "Không sửa số tồn hoặc trạng thái qua API thông tin lô. Hãy dùng phiếu nhập/xuất kho.");

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
        lot.Grade = NormalizeGrade(request.Grade);
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
        await EnsureLotAreaAsync(lot, ct);
        if (await _uow.FrozenStorageTransactions.AnyAsync(
                transaction => transaction.FrozenLotId == id, ct))
            throw AppException.Conflict("Không thể xóa lô đã có lịch sử nhập/xuất kho.");

        if (lot.Quantity != 0 || lot.WeightKg != 0)
            throw AppException.Conflict("Không thể xóa lô còn tồn kho.");
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
        var allLots = await ScopedLotsAsync(ct);
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
        var lots = await ScopedLotsAsync(ct);
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

        var lots = await ScopedLotsAsync(ct);
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

    public async Task<ApiResponse<IEnumerable<FrozenStorageTransactionDto>>> GetTransactionsAsync(
        CancellationToken ct = default)
    {
        var allowed = await _areas.AreaIdsAsync(ct);
        var transactions = (await _uow.FrozenStorageTransactions.GetAllAsync(ct))
            .Where(item => allowed is null || allowed.Contains(item.FarmingAreaId))
            .OrderByDescending(item => item.CreatedAt)
            .Select(MapTransaction)
            .ToList();
        return ApiResponse<IEnumerable<FrozenStorageTransactionDto>>.Ok(transactions);
    }

    public async Task<ApiResponse<FrozenStorageTransactionDto>> CreateTransactionAsync(
        CreateFrozenStorageTransactionRequest request,
        CancellationToken ct = default)
    {
        if (!string.Equals(request.Type, nameof(FrozenStorageTransactionType.Inbound), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Type, nameof(FrozenStorageTransactionType.Outbound), StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest("Type phải là Inbound hoặc Outbound.");
        var type = Enum.Parse<FrozenStorageTransactionType>(request.Type, ignoreCase: true);
        if (request.RequestedQuantity <= 0 || decimal.Round(request.RequestedWeightKg, 3) <= 0)
            throw AppException.BadRequest("Số lượng và trọng lượng dự kiến phải lớn hơn 0.");

        var lot = await _uow.FrozenLots.GetByIdAsync(request.FrozenLotId, ct)
            ?? throw AppException.NotFound("Frozen lot");
        await EnsureLotAreaAsync(lot, ct);
        if (lot.FarmingAreaId is not Guid areaId)
            throw AppException.Conflict("Lô đông chưa xác định khu sở hữu.");

        var transaction = new FrozenStorageTransaction
        {
            TransactionCode = NewTransactionCode(type),
            FrozenLotId = lot.Id,
            FarmingAreaId = areaId,
            Type = type,
            Status = FrozenStorageTransactionStatus.PendingApproval,
            RequestedQuantity = request.RequestedQuantity,
            RequestedWeightKg = decimal.Round(request.RequestedWeightKg, 3),
            CreatedByUserId = _currentUser.UserId,
            Notes = NormalizeOptionalText(request.Notes)
        };
        await _uow.FrozenStorageTransactions.AddAsync(transaction, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FrozenStorageTransactionDto>.Ok(
            MapTransaction(transaction), "Đã lập phiếu nhập/xuất kho, chờ Farm Manager duyệt.");
    }

    public async Task<ApiResponse<FrozenStorageTransactionDto>> ApproveTransactionAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var transaction = await GetTransactionAsync(id, ct);
        if (transaction.Status != FrozenStorageTransactionStatus.PendingApproval)
            throw AppException.Conflict("Chỉ phiếu đang chờ duyệt mới được duyệt.");
        if (transaction.CreatedByUserId == _currentUser.UserId)
            throw AppException.Forbidden("Người lập phiếu không được tự duyệt giao dịch kho.");
        transaction.Status = FrozenStorageTransactionStatus.Approved;
        transaction.ApprovedByUserId = _currentUser.UserId;
        transaction.ApprovedAt = DateTime.UtcNow;
        transaction.ApprovedQuantity = transaction.RequestedQuantity;
        transaction.ApprovedWeightKg = transaction.RequestedWeightKg;
        transaction.RejectionReason = null;
        transaction.ConcurrencyVersion++;
        _uow.FrozenStorageTransactions.Update(transaction);
        await SaveInventoryChangesAsync(ct);
        return ApiResponse<FrozenStorageTransactionDto>.Ok(
            MapTransaction(transaction), "Đã duyệt phiếu nhập/xuất kho.");
    }

    public async Task<ApiResponse<FrozenStorageTransactionDto>> RejectTransactionAsync(
        Guid id,
        RejectFrozenStorageTransactionRequest request,
        CancellationToken ct = default)
    {
        var reason = NormalizeOptionalText(request.Reason);
        if (reason is null)
            throw AppException.BadRequest("Cần ghi lý do từ chối.");
        var transaction = await GetTransactionAsync(id, ct);
        if (transaction.Status != FrozenStorageTransactionStatus.PendingApproval)
            throw AppException.Conflict("Chỉ phiếu đang chờ duyệt mới được từ chối.");
        if (transaction.CreatedByUserId == _currentUser.UserId)
            throw AppException.Forbidden("Người lập phiếu không được tự từ chối giao dịch kho.");
        transaction.Status = FrozenStorageTransactionStatus.Rejected;
        transaction.ApprovedByUserId = _currentUser.UserId;
        transaction.ApprovedAt = DateTime.UtcNow;
        transaction.RejectionReason = reason;
        transaction.ConcurrencyVersion++;
        _uow.FrozenStorageTransactions.Update(transaction);
        await SaveInventoryChangesAsync(ct);
        return ApiResponse<FrozenStorageTransactionDto>.Ok(
            MapTransaction(transaction), "Đã từ chối phiếu nhập/xuất kho.");
    }

    public async Task<ApiResponse<FrozenStorageTransactionDto>> ExecuteTransactionAsync(
        Guid id,
        ExecuteFrozenStorageTransactionRequest request,
        CancellationToken ct = default)
    {
        if (request.ActualQuantity <= 0 || request.ActualWeightKg <= 0)
            throw AppException.BadRequest("Số lượng và trọng lượng thực tế phải lớn hơn 0.");
        var actualWeight = decimal.Round(request.ActualWeightKg, 3);
        if (actualWeight <= 0)
            throw AppException.BadRequest("Trọng lượng thực tế phải đạt độ chính xác 0.001 kg.");
        var transaction = await GetTransactionAsync(id, ct);
        if (!Enum.IsDefined(transaction.Type))
            throw AppException.Conflict("Loại giao dịch kho không hợp lệ; cần đối soát dữ liệu.");
        if (transaction.Status != FrozenStorageTransactionStatus.Approved)
            throw AppException.Conflict("Chỉ phiếu đã được duyệt mới được thực hiện.");
        if (transaction.PerformedAt is not null
            || await _uow.FrozenInventoryAdjustments.AnyAsync(
                adjustment => adjustment.FrozenStorageTransactionId == id, ct))
            throw AppException.Conflict("Phiếu này đã cập nhật tồn kho.");

        var lot = await _uow.FrozenLots.GetByIdAsync(transaction.FrozenLotId, ct)
            ?? throw AppException.NotFound("Frozen lot");
        await EnsureLotAreaAsync(lot, ct);
        if (lot.FarmingAreaId != transaction.FarmingAreaId)
            throw AppException.Forbidden(
                "Phiếu nhập/xuất không thuộc khu sở hữu của lô đông.");

        var approvedQuantity = transaction.ApprovedQuantity
            ?? transaction.RequestedQuantity;
        var approvedWeight = transaction.ApprovedWeightKg
            ?? transaction.RequestedWeightKg;
        if (request.ActualQuantity > approvedQuantity
            || actualWeight > approvedWeight)
            throw AppException.Conflict(
                "Số lượng hoặc trọng lượng thực tế vượt mức đã duyệt. Cần lập và duyệt phiếu bổ sung.");

        var quantityVariance = request.ActualQuantity - approvedQuantity;
        var weightVariance = decimal.Round(actualWeight - approvedWeight, 3);
        var varianceReason = NormalizeOptionalText(request.VarianceReason);
        if ((quantityVariance < 0 || weightVariance < 0)
            && varianceReason is null)
            throw AppException.BadRequest(
                "Cần ghi lý do khi số lượng hoặc trọng lượng thực tế thấp hơn mức đã duyệt.");

        if (transaction.Type == FrozenStorageTransactionType.Inbound && lot.HarvestVoucherId is Guid sourceId)
            await ReserveHarvestCapacityAsync(lot, sourceId, request.ActualQuantity, actualWeight, ct);

        var quantityDelta = transaction.Type == FrozenStorageTransactionType.Inbound
            ? request.ActualQuantity
            : -request.ActualQuantity;
        var weightDelta = transaction.Type == FrozenStorageTransactionType.Inbound
            ? actualWeight
            : -actualWeight;
        var quantityAfter = lot.Quantity + quantityDelta;
        var weightAfter = lot.WeightKg + weightDelta;
        if (quantityAfter < 0 || weightAfter < 0)
            throw AppException.Conflict("Số tồn không đủ để thực hiện phiếu xuất kho.");
        if ((quantityAfter == 0) != (weightAfter == 0))
            throw AppException.Conflict(
                "Số lượng và trọng lượng tồn phải cùng về 0 khi xuất hết lô.");

        var trackedItems = (await _uow.FrozenCrabItems.FindAsync(x => x.FrozenLotId == lot.Id, ct)).ToList();
        var selectedIds = request.FrozenCrabItemIds ?? [];
        if (transaction.Type == FrozenStorageTransactionType.Inbound)
        {
            if (selectedIds.Count != 0)
                throw AppException.BadRequest("Không chọn barcode đã đăng ký cho phiếu nhập.");
            if (trackedItems.Count != 0)
                throw AppException.Conflict("Lô đã đăng ký barcode; tạo lô mới cho lần nhập tiếp theo.");
        }
        else if (trackedItems.Count != 0 || lot.HarvestVoucherId.HasValue)
        {
            if (trackedItems.Count == 0)
                throw AppException.Conflict("Cần đăng ký barcode cho lô có nguồn thu hoạch trước khi xuất.");
            if (trackedItems.Any(x => x.StockState is not ("Available" or "Dispatched")))
                throw AppException.Conflict("Cần đối soát barcode legacy trước khi xuất kho.");
            var available = trackedItems.Where(x => x.StockState == "Available").ToList();
            if (available.Count != lot.Quantity || decimal.Round(available.Sum(x => x.WeightGram) / 1000m, 3) != lot.WeightKg)
                throw AppException.Conflict("Tồn barcode không khớp tồn lô; cần đối soát.");
            if (selectedIds.Count != request.ActualQuantity || selectedIds.Distinct().Count() != selectedIds.Count)
                throw AppException.BadRequest("Chọn đúng số barcode thực tế xuất kho, không trùng.");
            var selected = available.Where(x => selectedIds.Contains(x.Id)).ToList();
            if (selected.Count != selectedIds.Count || decimal.Round(selected.Sum(x => x.WeightGram) / 1000m, 3) != actualWeight)
                throw AppException.Conflict("Barcode không khả dụng, sai lô hoặc không khớp trọng lượng xuất.");
            var remaining = available.Where(x => !selectedIds.Contains(x.Id)).ToList();
            if (decimal.Round(remaining.Sum(x => x.WeightGram) / 1000m, 3) != weightAfter)
                throw AppException.Conflict("Trọng lượng còn lại không khớp barcode; cần đối soát làm tròn.");
            foreach (var item in selected)
            {
                item.StockState = "Dispatched";
                item.DispatchedAt = DateTime.UtcNow;
                item.OutboundTransactionId = transaction.Id;
                _uow.FrozenCrabItems.Update(item);
            }
        }
        else if (selectedIds.Count != 0)
            throw AppException.BadRequest("Lô nhập tổng hợp không có barcode đã đăng ký.");

        var adjustment = new FrozenInventoryAdjustment
        {
            FrozenLotId = lot.Id,
            FrozenStorageTransactionId = transaction.Id,
            FarmingAreaId = transaction.FarmingAreaId,
            QuantityDelta = quantityDelta,
            WeightDeltaKg = decimal.Round(weightDelta, 3),
            QuantityBefore = lot.Quantity,
            QuantityAfter = quantityAfter,
            WeightBeforeKg = lot.WeightKg,
            WeightAfterKg = decimal.Round(weightAfter, 3),
            PerformedByUserId = _currentUser.UserId,
            Reason = varianceReason
                ?? NormalizeOptionalText(request.Notes)
                ?? transaction.Notes
        };
        lot.Quantity = adjustment.QuantityAfter;
        lot.WeightKg = adjustment.WeightAfterKg;
        lot.Status = lot.Quantity == 0
            ? FrozenLotStatus.Shipped
            : lot.Status == FrozenLotStatus.Shipped
                ? FrozenLotStatus.Available
                : lot.Status;
        transaction.ActualQuantity = request.ActualQuantity;
        transaction.ActualWeightKg = actualWeight;
        transaction.QuantityVariance = quantityVariance;
        transaction.WeightVarianceKg = weightVariance;
        transaction.VarianceReason = varianceReason;
        transaction.PerformedByUserId = _currentUser.UserId;
        transaction.PerformedAt = DateTime.UtcNow;
        transaction.Status = FrozenStorageTransactionStatus.Completed;
        transaction.Notes = NormalizeOptionalText(request.Notes) ?? transaction.Notes;
        lot.ConcurrencyVersion++;
        transaction.ConcurrencyVersion++;

        _uow.FrozenLots.Update(lot);
        _uow.FrozenStorageTransactions.Update(transaction);
        await _uow.FrozenInventoryAdjustments.AddAsync(adjustment, ct);
        await SaveInventoryChangesAsync(ct);
        return ApiResponse<FrozenStorageTransactionDto>.Ok(
            MapTransaction(transaction), "Đã thực hiện giao dịch và cập nhật tồn kho.");
    }

    // Serialize inbound against the shared completed harvest voucher, not only
    // each individual frozen lot. Current stock + all recorded outbound is the
    // cumulative received amount (including legacy opening stock).
    private async Task ReserveHarvestCapacityAsync(FrozenLot lot, Guid voucherId,
        int quantity, decimal weightKg, CancellationToken ct)
    {
        var voucher = await _uow.HarvestVouchers.GetByIdAsync(voucherId, ct)
            ?? throw AppException.Conflict("Không tìm thấy nguồn thu hoạch.");
        if (voucher.Status != HarvestStatus.Completed || voucher.FarmingAreaId != lot.FarmingAreaId
            || voucher.FarmingAreaId is null)
            throw AppException.Conflict("Nguồn thu hoạch chưa hoàn tất hoặc sai khu.");
        var lots = (await _uow.FrozenLots.FindAsync(x => x.HarvestVoucherId == voucherId, ct)).ToList();
        if (lots.Any(x => x.FarmingAreaId != voucher.FarmingAreaId))
            throw AppException.Conflict("Nguồn thu hoạch có lô đông sai khu; cần đối soát.");
        if (lots.All(x => x.Id != lot.Id)) lots.Add(lot);
        var ids = lots.Select(x => x.Id).ToHashSet();
        var outbound = await _uow.FrozenInventoryAdjustments.FindAsync(
            x => ids.Contains(x.FrozenLotId) && x.QuantityDelta < 0, ct);
        var receivedQuantity = lots.Sum(x => (long)x.Quantity) - outbound.Sum(x => (long)x.QuantityDelta);
        var receivedWeight = lots.Sum(x => x.WeightKg) - outbound.Sum(x => x.WeightDeltaKg);
        if (receivedQuantity + quantity > voucher.TotalQuantity ||
            receivedWeight + weightKg > voucher.TotalWeightKg)
            throw AppException.Conflict("Tổng nhập kho vượt số lượng hoặc trọng lượng thu hoạch đã xác nhận.");
        // Existing EF concurrency token protects simultaneous receipt across lots.
        voucher.ConcurrencyVersion++;
        _uow.HarvestVouchers.Update(voucher);
    }

    public async Task<ApiResponse<IEnumerable<FrozenInventoryAdjustmentDto>>> GetAdjustmentsAsync(
        Guid? frozenLotId = null,
        CancellationToken ct = default)
    {
        if (frozenLotId is Guid lotId)
        {
            var lot = await _uow.FrozenLots.GetByIdAsync(lotId, ct)
                ?? throw AppException.NotFound("Frozen lot");
            await EnsureLotAreaAsync(lot, ct);
        }
        var allowed = await _areas.AreaIdsAsync(ct);
        var rows = (await _uow.FrozenInventoryAdjustments.GetAllAsync(ct))
            .Where(item => (frozenLotId is null || item.FrozenLotId == frozenLotId)
                && (allowed is null || allowed.Contains(item.FarmingAreaId)))
            .OrderByDescending(item => item.CreatedAt)
            .Select(MapAdjustment)
            .ToList();
        return ApiResponse<IEnumerable<FrozenInventoryAdjustmentDto>>.Ok(rows);
    }

    public async Task<ApiResponse<IEnumerable<FrozenInventoryOpeningBalanceDto>>>
        GetOpeningBalancesAsync(CancellationToken ct = default)
    {
        var reviewer = await _areas.CurrentUserAsync(ct);
        if (reviewer is not null
            && reviewer.Role is not (UserRole.FarmAdministrator or UserRole.FarmManager))
            throw AppException.Forbidden(
                "Chỉ Farm Administrator hoặc Farm Manager được rà soát số dư đầu kỳ.");
        var allowed = await _areas.AreaIdsAsync(ct);
        var candidates = await _uow.FrozenInventoryOpeningBalances.GetAllAsync(ct);
        var balances = new List<FrozenInventoryOpeningBalance>();
        foreach (var item in candidates)
        {
            var lot = await _uow.FrozenLots.GetByIdAsync(item.FrozenLotId, ct);
            if (lot is null) continue;
            var evidence = await ResolveOpeningBalanceAreaEvidenceAsync(
                lot, item.FarmingAreaId, ct);
            if (!evidence.Conflicting
                && evidence.AreaId is Guid evidenceAreaId
                && (allowed is null || allowed.Contains(evidenceAreaId)))
                balances.Add(item);
        }
        balances = balances
            .OrderBy(item => item.VerificationStatus)
            .ThenBy(item => item.OpeningDate)
            .ToList();

        var result = new List<FrozenInventoryOpeningBalanceDto>(balances.Count);
        foreach (var balance in balances)
            result.Add(await MapOpeningBalanceAsync(balance, ct));

        return ApiResponse<IEnumerable<FrozenInventoryOpeningBalanceDto>>.Ok(result);
    }

    public async Task<ApiResponse<FrozenInventoryOpeningBalanceDto>>
        AssignOpeningBalanceAreaAsync(
            Guid id,
            AssignFrozenOpeningBalanceAreaRequest request,
            CancellationToken ct = default)
    {
        var user = await _areas.CurrentUserAsync(ct);
        if (user is not null
            && user.Role is not (UserRole.FarmAdministrator or UserRole.FarmManager))
            throw AppException.Forbidden(
                "Chỉ Farm Administrator hoặc Farm Manager được rà soát khu của số dư.");
        if (request.FarmingAreaId == Guid.Empty)
            throw AppException.BadRequest("FarmingAreaId is required.");
        await _areas.EnsureAsync(request.FarmingAreaId, manage: false, ct);

        var reason = NormalizeOptionalText(request.Reason);
        if (reason is null)
            throw AppException.BadRequest("Cần ghi lý do và bằng chứng gán khu.");
        var balance = await _uow.FrozenInventoryOpeningBalances.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Frozen inventory opening balance");
        if (balance.VerificationStatus == FrozenOpeningBalanceVerificationStatus.Verified)
            throw AppException.Conflict("Số dư đã xác nhận là bất biến.");
        if (balance.FarmingAreaId.HasValue)
            throw AppException.Conflict("Số dư đã được gán khu; không được chuyển khu trực tiếp.");

        var lot = await _uow.FrozenLots.GetByIdAsync(balance.FrozenLotId, ct)
            ?? throw AppException.NotFound("Frozen lot");
        var evidence = await ResolveOpeningBalanceAreaEvidenceAsync(
            lot, balanceAreaId: null, ct);
        if (evidence.Conflicting)
            throw AppException.Conflict(
                "Bằng chứng khu của FrozenLot, HarvestVoucher hoặc ledger đang mâu thuẫn.");
        if (evidence.AreaId != request.FarmingAreaId)
            throw AppException.Conflict(
                "Không có bằng chứng lịch sử xác nhận khu được chọn; cần quy trình quản trị dữ liệu ngoại lệ.");

        var lotAreaBefore = lot.FarmingAreaId;
        if (!lot.FarmingAreaId.HasValue)
        {
            lot.FarmingAreaId = request.FarmingAreaId;
            _uow.FrozenLots.Update(lot);
        }
        balance.FarmingAreaId = request.FarmingAreaId;
        balance.VerificationNote =
            $"AreaEvidence={string.Join(",", evidence.Sources)}; ReviewReason={reason}";
        _uow.FrozenInventoryOpeningBalances.Update(balance);
        await ConfigAudit.WriteAsync(
            _uow,
            _currentUser.UserId,
            "AssignFrozenOpeningBalanceArea",
            nameof(FrozenInventoryOpeningBalance),
            balance.Id,
            $"Area={request.FarmingAreaId}; FrozenLotAreaBefore={lotAreaBefore}; "
            + $"Evidence={string.Join(",", evidence.Sources)}; Reason={reason}",
            ct);
        await SaveInventoryChangesAsync(ct);
        return ApiResponse<FrozenInventoryOpeningBalanceDto>.Ok(
            await MapOpeningBalanceAsync(balance, ct),
            "Đã gán khu theo bằng chứng lịch sử; số dư vẫn chờ Farm Manager xác nhận.");
    }

    private async Task<OpeningBalanceAreaEvidence> ResolveOpeningBalanceAreaEvidenceAsync(
        FrozenLot lot,
        Guid? balanceAreaId,
        CancellationToken ct)
    {
        var evidence = new List<(Guid AreaId, string Source)>();
        static void Add(
            List<(Guid AreaId, string Source)> target,
            Guid? areaId,
            string source)
        {
            if (areaId is Guid id && id != Guid.Empty)
                target.Add((id, source));
        }

        Add(evidence, balanceAreaId, "OpeningBalance");
        Add(evidence, lot.FarmingAreaId, "FrozenLot");
        if (lot.HarvestVoucherId is Guid voucherId)
        {
            var voucher = await _uow.HarvestVouchers.GetByIdAsync(voucherId, ct);
            Add(evidence, voucher?.FarmingAreaId, "HarvestVoucher");
            if (_uow.HarvestApprovedLines is { } approvedRepository)
            {
                var approved = await approvedRepository.FindAsync(
                    line => line.HarvestVoucherId == voucherId, ct);
                foreach (var line in approved)
                    Add(evidence, line.FarmingAreaId, "HarvestApprovedLine");
            }
        }

        if (_uow.FrozenStorageTransactions is { } transactionRepository)
        {
            var transactions = await transactionRepository.FindAsync(
                item => item.FrozenLotId == lot.Id, ct);
            foreach (var transaction in transactions)
                Add(evidence, transaction.FarmingAreaId, "FrozenStorageTransaction");
        }
        if (_uow.FrozenInventoryAdjustments is { } adjustmentRepository)
        {
            var adjustments = await adjustmentRepository.FindAsync(
                item => item.FrozenLotId == lot.Id, ct);
            foreach (var adjustment in adjustments)
                Add(evidence, adjustment.FarmingAreaId, "FrozenInventoryAdjustment");
        }

        var areas = evidence.Select(item => item.AreaId).Distinct().ToArray();
        return new OpeningBalanceAreaEvidence(
            areas.Length == 1 ? areas[0] : null,
            areas.Length > 1,
            evidence.Select(item => item.Source).Distinct().ToArray());
    }

    private sealed record OpeningBalanceAreaEvidence(
        Guid? AreaId,
        bool Conflicting,
        IReadOnlyList<string> Sources);

    public async Task<ApiResponse<FrozenInventoryOpeningBalanceDto>>
        VerifyOpeningBalanceAsync(
            Guid id,
            VerifyFrozenOpeningBalanceRequest request,
            CancellationToken ct = default)
    {
        await EnsureOpeningBalanceManagerAsync(ct);
        var balance = await _uow.FrozenInventoryOpeningBalances.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Frozen inventory opening balance");
        if (balance.VerificationStatus == FrozenOpeningBalanceVerificationStatus.Verified)
            throw AppException.Conflict("Số dư đầu kỳ đã được xác nhận và không thể sửa.");
        if (balance.FarmingAreaId is not Guid areaId || areaId == Guid.Empty)
            throw AppException.Conflict(
                "Số dư đầu kỳ chưa xác định khu và cần được quản trị dữ liệu rà soát.");
        await _areas.EnsureAsync(areaId, manage: false, ct);

        var reason = NormalizeOptionalText(request.Reason);
        if (reason is null)
            throw AppException.BadRequest("Cần ghi lý do xác nhận số dư đầu kỳ.");
        var openingWeight = decimal.Round(request.OpeningWeightKg, 3);
        if (request.OpeningQuantity < 0 || openingWeight < 0)
            throw AppException.BadRequest("Số dư đầu kỳ không được âm.");
        if ((request.OpeningQuantity == 0) != (openingWeight == 0))
            throw AppException.BadRequest(
                "Số lượng và trọng lượng đầu kỳ phải cùng bằng 0 hoặc cùng lớn hơn 0.");

        var lot = await _uow.FrozenLots.GetByIdAsync(balance.FrozenLotId, ct)
            ?? throw AppException.NotFound("Frozen lot");
        if (lot.FarmingAreaId.HasValue && lot.FarmingAreaId != areaId)
            throw AppException.Conflict("Khu của số dư đầu kỳ không khớp lô đông.");

        var adjustments = await _uow.FrozenInventoryAdjustments.FindAsync(
            item => item.FrozenLotId == balance.FrozenLotId, ct);
        var quantityDelta = adjustments.Sum(item => item.QuantityDelta);
        var weightDelta = decimal.Round(
            adjustments.Sum(item => item.WeightDeltaKg), 3);
        var expectedQuantity = request.OpeningQuantity + quantityDelta;
        var expectedWeight = decimal.Round(openingWeight + weightDelta, 3);
        if (expectedQuantity != lot.Quantity || expectedWeight != lot.WeightKg)
            throw AppException.Conflict(
                "Số dư đề nghị không đối soát được với tồn hiện tại và các biến động đã ghi.");

        var previous = $"{balance.OpeningQuantity}/{balance.OpeningWeightKg:0.###}/"
            + balance.VerificationStatus;
        balance.OpeningQuantity = request.OpeningQuantity;
        balance.OpeningWeightKg = openingWeight;
        balance.VerificationStatus = FrozenOpeningBalanceVerificationStatus.Verified;
        balance.VerifiedByUserId = _currentUser.UserId;
        balance.VerifiedAt = DateTime.UtcNow;
        balance.VerificationNote = reason;
        _uow.FrozenInventoryOpeningBalances.Update(balance);
        await ConfigAudit.WriteAsync(
            _uow,
            _currentUser.UserId,
            "VerifyFrozenOpeningBalance",
            nameof(FrozenInventoryOpeningBalance),
            balance.Id,
            $"Before={previous}; After={balance.OpeningQuantity}/"
            + $"{balance.OpeningWeightKg:0.###}/Verified; Reason={reason}",
            ct);
        await SaveInventoryChangesAsync(ct);

        return ApiResponse<FrozenInventoryOpeningBalanceDto>.Ok(
            await MapOpeningBalanceAsync(balance, ct),
            "Đã xác nhận số dư đầu kỳ; tồn hiện tại không bị thay đổi.");
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
        await EnsureLotAreaAsync(lot, ct);

        if (lot.HarvestVoucherId is not Guid voucherId || voucherId == Guid.Empty)
            throw AppException.BadRequest(
                "The frozen lot must be linked to a harvest voucher before registering crab items.");

        var voucher = await _uow.HarvestVouchers.GetByIdAsync(voucherId, ct)
            ?? throw AppException.NotFound("Harvest voucher");
        if (voucher.Status != HarvestStatus.Completed)
            throw AppException.Conflict(
                "Chỉ được đăng ký sản phẩm đông từ phiếu thu hoạch đã được xác nhận hoàn tất.");
        if (voucher.FarmingAreaId is null || voucher.FarmingAreaId != lot.FarmingAreaId)
            throw AppException.Conflict("Phiếu thu hoạch và lô đông phải thuộc cùng khu.");

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
        if (decimal.Round(inputs.Sum(x => x.WeightGram) / 1000m, 3) != lot.WeightKg)
            throw AppException.BadRequest("Tổng trọng lượng barcode phải khớp trọng lượng tồn của lô.");
        if (inputs.Any(x => x.WeightGram <= 0 || decimal.Round(x.WeightGram, 2) != x.WeightGram))
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
                Grade = normalizedGrades[i],
                StockState = "Available"
            };

            createdItems.Add(item);
        }

        // Chỉ thêm dữ liệu sau khi đã kiểm tra hợp lệ toàn bộ request.
        foreach (var item in createdItems)
            await _uow.FrozenCrabItems.AddAsync(item, ct);

        // Serialize registration with concurrent inventory transactions.
        lot.ConcurrencyVersion++;
        _uow.FrozenLots.Update(lot);
        await SaveInventoryChangesAsync(ct);

        return ApiResponse<IEnumerable<FrozenCrabItemDto>>.Ok(
            createdItems.Select(MapToFrozenCrabItemDto).ToList(),
            "Frozen crab items and barcodes registered.");
    }

    public async Task<ApiResponse<IEnumerable<FrozenCrabItemDto>>> ListCrabItemsAsync(
        Guid lotId, CancellationToken ct = default)
    {
        var lot = await _uow.FrozenLots.GetByIdAsync(lotId, ct)
            ?? throw AppException.NotFound("Frozen lot");
        await EnsureLotAreaAsync(lot, ct);
        var items = await _uow.FrozenCrabItems.FindAsync(x => x.FrozenLotId == lotId, ct);
        return ApiResponse<IEnumerable<FrozenCrabItemDto>>.Ok(items.Select(MapToFrozenCrabItemDto).ToList());
    }

    public async Task<ApiResponse<IEnumerable<FrozenCrabItemDto>>> ReconcileCrabItemsAsync(
        Guid lotId, ReconcileFrozenItemsRequest request, CancellationToken ct = default)
    {
        var actor = await _areas.CurrentUserAsync(ct);
        if (actor?.Role != UserRole.FarmManager)
            throw AppException.Forbidden("Chỉ Farm Manager được đối soát barcode.");
        var lot = await _uow.FrozenLots.GetByIdAsync(lotId, ct)
            ?? throw AppException.NotFound("Frozen lot");
        await EnsureLotAreaAsync(lot, ct);
        var reason = NormalizeOptionalText(request.Reason);
        if (reason is null || reason.Length > 1000 || request.AvailableItemIds is null)
            throw AppException.BadRequest("Cần danh sách barcode còn thực tế và lý do đối soát (tối đa 1000 ký tự).");
        var items = (await _uow.FrozenCrabItems.FindAsync(x => x.FrozenLotId == lotId, ct)).ToList();
        if (items.Count == 0 || items.Any(x => x.StockState != "Unverified"))
            throw AppException.Conflict("Chỉ đối soát lô barcode legacy chưa xác minh; không ghi đè lịch sử đã xác nhận.");
        var ids = request.AvailableItemIds.ToHashSet();
        var available = items.Where(x => ids.Contains(x.Id)).ToList();
        if (ids.Count != request.AvailableItemIds.Count || available.Count != ids.Count
            || available.Count != lot.Quantity || decimal.Round(available.Sum(x => x.WeightGram) / 1000m, 3) != lot.WeightKg)
            throw AppException.Conflict("Barcode còn kho phải khớp số lượng/trọng lượng tồn, không trùng hoặc sai lô.");
        foreach (var item in items)
        {
            item.StockState = ids.Contains(item.Id) ? "Available" : "Dispatched";
            // Historical dispatch timestamp and transaction are unknown; never fabricate.
            _uow.FrozenCrabItems.Update(item);
        }
        lot.ConcurrencyVersion++;
        _uow.FrozenLots.Update(lot);
        await ConfigAudit.WriteAsync(_uow, actor.Id, "ReconcileFrozenItems", "FrozenLot", lot.Id,
            System.Text.Json.JsonSerializer.Serialize(new { reason, availableItemIds = ids }), ct);
        await SaveInventoryChangesAsync(ct);
        return ApiResponse<IEnumerable<FrozenCrabItemDto>>.Ok(items.Select(MapToFrozenCrabItemDto).ToList());
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
        var itemLot = await _uow.FrozenLots.GetByIdAsync(item.FrozenLotId, ct)
            ?? throw AppException.NotFound("Frozen lot");
        await EnsureLotAreaAsync(itemLot, ct);

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
    var itemLot = await _uow.FrozenLots.GetByIdAsync(item.FrozenLotId, ct)
        ?? throw AppException.NotFound("Frozen lot");
    await EnsureLotAreaAsync(itemLot, ct);

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
            item.Grade,
            item.StockState,
            item.DispatchedAt,
            item.OutboundTransactionId);
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
            requirePositiveInventory: false,
            request.WeightKg,
            request.Quantity,
            request.Grade);
        if (request.WeightKg != 0 || request.Quantity != 0)
            throw AppException.BadRequest(
                "POST /api/frozen-lots chỉ tạo thông tin lô. Số tồn phải được ghi qua phiếu nhập kho.");
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
            requirePositiveInventory: true,
            request.WeightKg,
            request.Quantity,
            request.Grade);
    }

    private static void ValidateCommonFields(
        DateTime frozenDate,
        DateTime expiryDate,
        bool requirePositiveInventory,
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

        if (requirePositiveInventory && weightKg < 0)
        {
            throw AppException.BadRequest(
                "Trọng lượng tồn không được âm.");
        }

        if (requirePositiveInventory && quantity < 0)
        {
            throw AppException.BadRequest(
                "Số lượng tồn không được âm.");
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
            lot.UpdatedAt,
            lot.FarmingAreaId);
    }

    private async Task<List<FrozenLot>> ScopedLotsAsync(CancellationToken ct)
    {
        var allowed = await _areas.AreaIdsAsync(ct);
        return (await _uow.FrozenLots.GetAllAsync(ct))
            .Where(lot => allowed is null
                || (lot.FarmingAreaId is Guid areaId && allowed.Contains(areaId)))
            .ToList();
    }

    private async Task EnsureLotAreaAsync(FrozenLot lot, CancellationToken ct)
    {
        if (lot.FarmingAreaId is not Guid areaId || areaId == Guid.Empty)
            throw AppException.Forbidden("Lô đông chưa xác định khu sở hữu.");
        await _areas.EnsureAsync(areaId, manage: false, ct);
    }

    private async Task<FrozenStorageTransaction> GetTransactionAsync(
        Guid id, CancellationToken ct)
    {
        var transaction = await _uow.FrozenStorageTransactions.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Frozen storage transaction");
        await _areas.EnsureAsync(transaction.FarmingAreaId, manage: false, ct);
        return transaction;
    }

    private static string NewTransactionCode(FrozenStorageTransactionType type)
    {
        var prefix = type == FrozenStorageTransactionType.Inbound ? "IN" : "OUT";
        return $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..30];
    }

    private static FrozenStorageTransactionDto MapTransaction(
        FrozenStorageTransaction transaction) => new(
        transaction.Id,
        transaction.TransactionCode,
        transaction.FrozenLotId,
        transaction.FarmingAreaId,
        transaction.Type.ToString(),
        transaction.Status.ToString(),
        transaction.RequestedQuantity,
        transaction.RequestedWeightKg,
        transaction.ActualQuantity,
        transaction.ActualWeightKg,
        transaction.CreatedByUserId,
        transaction.ApprovedByUserId,
        transaction.ApprovedAt,
        transaction.RejectionReason,
        transaction.PerformedByUserId,
        transaction.PerformedAt,
        transaction.Notes,
        transaction.CreatedAt,
        transaction.ApprovedQuantity,
        transaction.ApprovedWeightKg,
        transaction.QuantityVariance,
        transaction.WeightVarianceKg,
        transaction.VarianceReason);

    private async Task SaveInventoryChangesAsync(CancellationToken ct)
    {
        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict(
                "Dữ liệu kho vừa được thay đổi bởi giao dịch khác. Vui lòng tải lại trước khi tiếp tục.");
        }
        catch (DbUpdateException exception) when (
            exception.Entries.Any(entry => entry.Entity is FrozenInventoryAdjustment))
        {
            throw AppException.Conflict(
                "Giao dịch kho đã được xử lý hoặc dữ liệu tồn kho vừa thay đổi.");
        }
    }

    private static FrozenInventoryAdjustmentDto MapAdjustment(
        FrozenInventoryAdjustment adjustment) => new(
        adjustment.Id,
        adjustment.FrozenLotId,
        adjustment.FrozenStorageTransactionId,
        adjustment.FarmingAreaId,
        adjustment.QuantityDelta,
        adjustment.WeightDeltaKg,
        adjustment.QuantityBefore,
        adjustment.QuantityAfter,
        adjustment.WeightBeforeKg,
        adjustment.WeightAfterKg,
        adjustment.PerformedByUserId,
        adjustment.Reason,
        adjustment.CreatedAt);

    private async Task<FrozenInventoryOpeningBalanceDto> MapOpeningBalanceAsync(
        FrozenInventoryOpeningBalance balance,
        CancellationToken ct)
    {
        var lot = await _uow.FrozenLots.GetByIdAsync(balance.FrozenLotId, ct)
            ?? throw AppException.NotFound("Frozen lot");
        var adjustments = await _uow.FrozenInventoryAdjustments.FindAsync(
            item => item.FrozenLotId == balance.FrozenLotId, ct);
        var quantityDelta = adjustments.Sum(item => item.QuantityDelta);
        var weightDelta = decimal.Round(
            adjustments.Sum(item => item.WeightDeltaKg), 3);
        var expectedQuantity = balance.OpeningQuantity + quantityDelta;
        var expectedWeight = decimal.Round(balance.OpeningWeightKg + weightDelta, 3);

        return new FrozenInventoryOpeningBalanceDto(
            balance.Id,
            balance.FrozenLotId,
            balance.FarmingAreaId,
            balance.OpeningQuantity,
            balance.OpeningWeightKg,
            balance.OpeningDate,
            balance.Source,
            balance.VerificationStatus.ToString(),
            balance.VerifiedByUserId,
            balance.VerifiedAt,
            balance.VerificationNote,
            quantityDelta,
            weightDelta,
            expectedQuantity,
            expectedWeight,
            lot.Quantity,
            lot.WeightKg,
            expectedQuantity == lot.Quantity && expectedWeight == lot.WeightKg);
    }

    private async Task EnsureOpeningBalanceManagerAsync(CancellationToken ct)
    {
        var user = await _areas.CurrentUserAsync(ct);
        if (user is not null && user.Role != UserRole.FarmManager)
            throw AppException.Forbidden(
                "Chỉ Farm Manager được rà soát và xác nhận số dư đầu kỳ.");
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
