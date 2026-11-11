using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Service tổng hợp báo cáo tồn kho cua đông lạnh.
/// </summary>
public class InventoryReportService : IInventoryReportService
{
    private readonly IUnitOfWork _uow;
    private readonly ReportAreaGate _areas;

    public InventoryReportService(IUnitOfWork uow, ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _areas = new ReportAreaGate(uow, currentUser);
    }

    /// <summary>
    /// Lấy báo cáo tồn kho với bộ lọc mặc định:
    /// không chọn ngày tham chiếu, cảnh báo trong 30 ngày,
    /// không lọc theo lô cua nhập.
    /// </summary>
    public async Task<InventoryReportDto> GetInventoryReportAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetInventoryReportAsync(
            new InventoryReportFilterDto(null, 30),
            cancellationToken);
    }

    /// <summary>
    /// Lấy báo cáo tồn kho theo bộ lọc người dùng cung cấp.
    /// Nếu có CrabLotId, số lượng và khối lượng được tính từ các cua
    /// thuộc lô nhập đó, kể cả khi chúng nằm chung trong một lô cấp đông.
    /// </summary>
    public async Task<InventoryReportDto> GetInventoryReportAsync(
        InventoryReportFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        // Không thể tiếp tục xử lý nếu không có bộ lọc.
        if (filter is null)
        {
            throw AppException.BadRequest(
                "Inventory report filter is required.");
        }

        // Chỉ kiểm tra tồn tại khi người dùng có chọn lô cua nhập.
        // Nếu không chọn CrabLotId, báo cáo bao gồm tất cả các lô.
        if (filter.CrabLotId.HasValue)
        {
            var lotExists = await _uow.CrabLots.AnyAsync(
                lot => lot.Id == filter.CrabLotId.Value,
                cancellationToken);

            if (!lotExists)
            {
                throw AppException.BadRequest(
                    "Crab lot does not exist.");
            }
        }

        // Lấy danh sách các lô cấp đông trong khu được phân công.
        var lots = await LimitLotsAsync(
            await _uow.FrozenLots.GetAllAsync(cancellationToken),
            cancellationToken);

        // ============================================================
        // 1. GIỚI HẠN DỮ LIỆU THEO THỜI ĐIỂM
        // ============================================================

        // scopedLots là các lô cấp đông nằm trong phạm vi ngày cần báo cáo.
        IEnumerable<Domain.Entities.FrozenLot> scopedLots = lots;

        if (filter.AsOfDate.HasValue)
        {
            // Cộng thêm một ngày để bao gồm toàn bộ ngày AsOfDate.
            var asOf = filter.AsOfDate.Value.Date.AddDays(1);

            scopedLots = lots.Where(lot =>
                lot.FrozenDate < asOf);
        }

        // Ngày này được dùng để xác định lô đã hết hạn hay chưa.
        var referenceDate = filter.AsOfDate?.Date
            ?? DateTime.UtcNow.Date;

        // ============================================================
        // 2. KIỂM TRA SỐ NGÀY CẢNH BÁO HẾT HẠN
        // ============================================================

        var warningDays = filter.ExpiryWarningDays ?? 30;

        // Chỉ chấp nhận khoảng cảnh báo từ 0 đến 365 ngày.
        if (warningDays < 0 || warningDays > 365)
        {
            throw AppException.BadRequest(
                "ExpiryWarningDays must be between 0 and 365.");
        }

        // Danh sách item thuộc CrabLotId được chọn.
        // Danh sách để rỗng nếu không chọn lô; khi đó service giữ
        // cách tính cũ dựa trên số liệu tổng của FrozenLot.
        var matchingItems =
            new List<Domain.Entities.FrozenCrabItem>();

        if (filter.CrabLotId.HasValue)
        {
            // Tìm các cá thể cua thuộc lô nhập đã chọn.
            var selectedCrabs = await _uow.Crabs.FindAsync(
                crab => crab.CrabLotId == filter.CrabLotId.Value,
                cancellationToken);

            // Dùng HashSet để tra cứu CrabId nhanh khi lọc item.
            var selectedCrabIds = selectedCrabs
                .Select(crab => crab.Id)
                .ToHashSet();

            // Chỉ truy vấn item thuộc những lô cấp đông nằm trong
            // phạm vi ngày đã xác định ở trên.
            var scopedLotIds = scopedLots
                .Select(lot => lot.Id)
                .ToHashSet();

            var frozenItems = await _uow.FrozenCrabItems.FindAsync(
                item => scopedLotIds.Contains(item.FrozenLotId),
                cancellationToken);

            // Một lô cấp đông có thể chứa cua từ nhiều lô nhập.
            // Chỉ giữ item có CrabId thuộc lô nhập đang được lọc.
            matchingItems = frozenItems
                .Where(item => selectedCrabIds.Contains(item.CrabId))
                .ToList();
        }

        // ============================================================
        // 3. XÁC ĐỊNH CÁC LÔ ĐANG TỒN KHO
        // ============================================================

        // Tồn kho thực tế gồm lô Available hoặc Reserved và chưa hết hạn.
        var inventoryLots = scopedLots
            .Where(lot =>
                (lot.Status == FrozenLotStatus.Available ||
                 lot.Status == FrozenLotStatus.Reserved) &&
                lot.ExpiryDate.Date >= referenceDate)
            .ToList();

        // Nhóm item khớp theo lô cấp đông để có thể xác định:
        // lô nào có ít nhất một cua thuộc lô nhập được chọn.
        var itemsByLotId = matchingItems
            .GroupBy(item => item.FrozenLotId)
            .ToDictionary(
                group => group.Key,
                group => group.ToList());

        // Khi có lọc lô nhập, chỉ giữ lô cấp đông đang tồn kho
        // có chứa ít nhất một item phù hợp.
        var filteredInventoryLots = inventoryLots
            .Where(lot => itemsByLotId.ContainsKey(lot.Id))
            .ToList();

        // Các item thuộc những lô cấp đông đang tồn kho và khớp filter.
        var matchingInventoryItems = filteredInventoryLots
            .SelectMany(lot => itemsByLotId[lot.Id])
            .ToList();

        // Danh sách lô liên quan đến CrabLotId.
        // Không lọc CrabLotId thì dùng toàn bộ lô trong scopedLots.
        var matchingScopedLots = filter.CrabLotId.HasValue
            ? scopedLots
                .Where(lot => itemsByLotId.ContainsKey(lot.Id))
                .ToList()
            : scopedLots.ToList();

        // Tra cứu lô cấp đông theo ID, dùng để lấy ngày cấp đông
        // trong phần thống kê xu hướng.
        var inventoryLotById = inventoryLots.ToDictionary(
            lot => lot.Id);

        // Đếm số lô cấp đông đã xuất kho.
        // Khi lọc lô nhập, chỉ đếm lô có item thuộc lô đó.
        var shippedLots = matchingScopedLots.Count(lot =>
            lot.Status == FrozenLotStatus.Shipped);

        // Đếm lô đã được đánh dấu hết hạn hoặc có ngày hết hạn
        // trước ngày tham chiếu.
        var expiredLots = matchingScopedLots.Count(lot =>
            lot.Status == FrozenLotStatus.Expired ||
            lot.ExpiryDate.Date < referenceDate);

        // Đếm lô có trạng thái Available.
        var availableLots = filter.CrabLotId.HasValue
            ? filteredInventoryLots.Count(lot =>
                lot.Status == FrozenLotStatus.Available)
            : inventoryLots.Count(lot =>
                lot.Status == FrozenLotStatus.Available);

        // Đếm lô có trạng thái Reserved.
        var reservedLots = filter.CrabLotId.HasValue
            ? filteredInventoryLots.Count(lot =>
                lot.Status == FrozenLotStatus.Reserved)
            : inventoryLots.Count(lot =>
                lot.Status == FrozenLotStatus.Reserved);

        // ============================================================
        // 4. TÍNH TỔNG QUAN TỒN KHO
        // ============================================================

        // Mỗi FrozenLot chỉ được đếm một lần, dù có nhiều item khớp.
        var totalInventoryLots = filter.CrabLotId.HasValue
            ? filteredInventoryLots.Count
            : inventoryLots.Count;

        // Khi lọc lô cua nhập, cộng cân nặng từng item (gram -> kg).
        // Không lọc thì giữ nguyên tổng WeightKg của FrozenLot.
        var totalInventoryWeightKg = filter.CrabLotId.HasValue
            ? matchingInventoryItems.Sum(item => item.WeightGram) / 1000m
            : inventoryLots.Sum(lot => lot.WeightKg);

        // Khi lọc, mỗi FrozenCrabItem đại diện cho một cá thể cua.
        // Không lọc thì dùng tổng Quantity đã lưu ở FrozenLot.
        var totalInventoryQuantity = filter.CrabLotId.HasValue
            ? matchingInventoryItems.Count
            : inventoryLots.Sum(lot => lot.Quantity);

        // ============================================================
        // 5. THỐNG KÊ TỒN KHO THEO GRADE
        // ============================================================

        List<InventoryByGradeDto> byGrade;

        if (filter.CrabLotId.HasValue)
        {
            // Khi lọc lô nhập, dùng grade và trọng lượng snapshot
            // của từng item để không cộng nhầm cua từ lô khác.
            byGrade = matchingInventoryItems
                .GroupBy(item =>
                    string.IsNullOrWhiteSpace(item.Grade)
                        ? "Unknown"
                        : item.Grade)
                .Select(group => new InventoryByGradeDto(
                    Grade: group.Key,

                    // Đếm số FrozenLot khác nhau có item thuộc grade này.
                    LotCount: group
                        .Select(item => item.FrozenLotId)
                        .Distinct()
                        .Count(),

                    // Mỗi item tương ứng một con cua.
                    Quantity: group.Count(),

                    // Đổi tổng khối lượng từ gram sang kilogram.
                    WeightKg: group.Sum(item => item.WeightGram) / 1000m))
                .OrderBy(item => item.Grade)
                .ToList();
        }
        else
        {
            // Không lọc lô nhập thì giữ cách tổng hợp cũ
            // từ thông tin đã lưu trên FrozenLot.
            byGrade = inventoryLots
                .GroupBy(lot =>
                    string.IsNullOrWhiteSpace(lot.Grade)
                        ? "Unknown"
                        : lot.Grade)
                .Select(group => new InventoryByGradeDto(
                    Grade: group.Key,

                    // Số lô cấp đông thuộc grade.
                    LotCount: group.Count(),

                    // Tổng số cua của các lô cấp đông.
                    Quantity: group.Sum(lot => lot.Quantity),

                    // Tổng khối lượng của các lô cấp đông.
                    WeightKg: group.Sum(lot => lot.WeightKg)
                ))
                .OrderBy(item => item.Grade)
                .ToList();
        }

        // Mốc cuối của khoảng cảnh báo hết hạn.
        var expiryCutoff = referenceDate.AddDays(warningDays);

        // Các lô có thể đưa vào danh sách cảnh báo.
        // Lô đã Shipped không được xem là hàng có thể bán/đặt trước.
        var expiryCandidates = scopedLots
            .Where(lot =>
                lot.Status == FrozenLotStatus.Available ||
                lot.Status == FrozenLotStatus.Reserved)
            .ToList();

        List<InventoryExpiryAlertDto> expiryAlerts;

        if (filter.CrabLotId.HasValue)
        {
            // Chỉ giữ lô cấp đông liên quan đến lô cua nguồn
            // và đang Available hoặc Reserved.
            var expiryLotIds = matchingScopedLots
                .Where(lot =>
                    lot.Status == FrozenLotStatus.Available ||
                    lot.Status == FrozenLotStatus.Reserved)
                .Select(lot => lot.Id)
                .ToHashSet();

            // Nhóm theo lô cấp đông và grade để một lô có nhiều
            // grade có thể hiển thị thành các dòng cảnh báo riêng.
            expiryAlerts = matchingItems
                .Where(item => expiryLotIds.Contains(item.FrozenLotId))
                .GroupBy(item => new
                {
                    item.FrozenLotId,
                    Grade = string.IsNullOrWhiteSpace(item.Grade)
                        ? "Unknown"
                        : item.Grade
                })
                .Where(group =>
                    group.Min(item => item.ExpiryDate).Date <= expiryCutoff)
                .Select(group =>
                {
                    var firstItem = group.First();
                    var expired =
                        firstItem.ExpiryDate.Date < referenceDate;

                    return new InventoryExpiryAlertDto(
                        LotCode: firstItem.LotCode,
                        Grade: group.Key.Grade,
                        WeightKg:
                            group.Sum(item => item.WeightGram) / 1000m,
                        Quantity: group.Count(),
                        ExpiryDate: firstItem.ExpiryDate,
                        DaysUntilExpiry: Math.Max(
                            0,
                            (firstItem.ExpiryDate.Date - referenceDate).Days),
                        Status: expired ? "expired" : "expiring_soon");
                })
                .OrderBy(alert => alert.DaysUntilExpiry)
                .ToList();
        }
        else
        {
            // Không lọc lô nhập thì cảnh báo theo tổng số liệu
            // của từng FrozenLot như trước.
            expiryAlerts = expiryCandidates
                .Where(lot => lot.ExpiryDate.Date <= expiryCutoff)
                .Select(lot => new InventoryExpiryAlertDto(
                    LotCode: lot.LotCode,
                    Grade: lot.Grade ?? "Unknown",
                    WeightKg: lot.WeightKg,
                    Quantity: lot.Quantity,
                    ExpiryDate: lot.ExpiryDate,
                    DaysUntilExpiry: Math.Max(
                        0,
                        (lot.ExpiryDate.Date - referenceDate).Days),
                    Status: lot.ExpiryDate.Date < referenceDate
                        ? "expired"
                        : "expiring_soon"))
                .OrderBy(alert => alert.DaysUntilExpiry)
                .ToList();
        }

        // ============================================================
        // 6. THỐNG KÊ XU HƯỚNG TỒN KHO THEO NGÀY CẤP ĐÔNG
        // ============================================================

        List<InventoryTrendDto> trend;

        if (filter.CrabLotId.HasValue)
        {
            // Nhóm item theo ngày FrozenLot được cấp đông.
            // Số lượng và khối lượng chỉ tính cua thuộc lô nhập đã chọn.
            trend = matchingInventoryItems
                .GroupBy(item =>
                    inventoryLotById[item.FrozenLotId].FrozenDate.Date)
                .Select(group => new InventoryTrendDto(
                    Date: group.Key,

                    // Một lô cấp đông chỉ được đếm một lần trong ngày.
                    LotCount: group
                        .Select(item => item.FrozenLotId)
                        .Distinct()
                        .Count(),

                    // Tổng cân nặng item trong nhóm, đổi gram sang kg.
                    WeightKg: group.Sum(item => item.WeightGram) / 1000m,

                    // Mỗi item tương ứng một con cua.
                    Quantity: group.Count()))
                .OrderBy(item => item.Date)
                .ToList();
        }
        else
        {
            // Không lọc lô nhập thì giữ thống kê theo tổng FrozenLot.
            trend = inventoryLots
                .GroupBy(lot => lot.FrozenDate.Date)
                .Select(group => new InventoryTrendDto(
                    Date: group.Key,
                    LotCount: group.Count(),
                    WeightKg: group.Sum(lot => lot.WeightKg),
                    Quantity: group.Sum(lot => lot.Quantity)
                ))
                .OrderBy(item => item.Date)
                .ToList();
        }

        // ============================================================
        // 7. TRẢ KẾT QUẢ BÁO CÁO
        // ============================================================

        return new InventoryReportDto(
            TotalInventoryLots: totalInventoryLots,
            TotalInventoryWeightKg: totalInventoryWeightKg,
            TotalInventoryQuantity: totalInventoryQuantity,
            AvailableLots: availableLots,
            ReservedLots: reservedLots,
            ShippedLots: shippedLots,
            ExpiredLots: expiredLots,
            ByGrade: byGrade,
            ExpiryAlerts: expiryAlerts,
            Trend: trend);
    }

    private async Task<IEnumerable<Domain.Entities.FrozenLot>> LimitLotsAsync(
        IEnumerable<Domain.Entities.FrozenLot> lots,
        CancellationToken cancellationToken)
    {
        var allowed = await _areas.AreasAsync(null, cancellationToken);
        if (allowed is null) return lots;
        var voucherAreas = (await _uow.HarvestVouchers.GetAllAsync(cancellationToken))
            .Where(voucher => voucher.FarmingAreaId is Guid)
            .ToDictionary(voucher => voucher.Id, voucher => voucher.FarmingAreaId!.Value);
        return lots.Where(lot =>
            lot.HarvestVoucherId is Guid voucherId
            && voucherAreas.TryGetValue(voucherId, out var areaId)
            && allowed.Contains(areaId));
    }
}