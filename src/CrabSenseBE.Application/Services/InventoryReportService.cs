using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Service xử lý báo cáo tồn kho cua đông lạnh.
/// </summary>
public class InventoryReportService : IInventoryReportService
{
    private readonly IUnitOfWork _uow;

    public InventoryReportService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    /// <summary>
    /// Lấy báo cáo tồn kho cua đông lạnh.
    /// </summary>
    public async Task<InventoryReportDto> GetInventoryReportAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetInventoryReportAsync(
            new InventoryReportFilterDto(null, 30), cancellationToken);
    }
    /// <summary>
    /// Báo cáo tồn kho với filter thời gian + cảnh báo hết hạn.
    /// </summary>
    public async Task<InventoryReportDto> GetInventoryReportAsync(
        InventoryReportFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        // Lấy tất cả các lô đông lạnh từ database.
        var lots = await _uow.FrozenLots.GetAllAsync(cancellationToken);

        // ============================================================
        // 1. LỌC THEO THỜI ĐIỂM (nếu có AsOfDate)
        // ============================================================
        IEnumerable<Domain.Entities.FrozenLot> scopedLots = lots;

        if (filter.AsOfDate.HasValue)
        {
            var asOf = filter.AsOfDate.Value.Date.AddDays(1);

            scopedLots = lots.Where(l =>
                l.FrozenDate < asOf);
        }

        var referenceDate = filter.AsOfDate?.Date
            ?? DateTime.UtcNow.Date;

        // ============================================================
        //  CẢNH BÁO HẾT HẠN
        // ============================================================
        var warningDays = filter.ExpiryWarningDays ?? 30;

        if (warningDays < 0 || warningDays > 365)
        {
            throw AppException.BadRequest(
                "ExpiryWarningDays must be between 0 and 365.");
        }

        // ============================================================
        // 2. PHÂN LOẠI THEO TRẠNG THÁI
        // ============================================================

        // Có thể bán hoặc đang được giữ chỗ.
        // Đây mới là tồn kho thực tế.
        var inventoryLots = scopedLots
            .Where(l =>
                (l.Status == FrozenLotStatus.Available ||
                 l.Status == FrozenLotStatus.Reserved) &&
                l.ExpiryDate.Date >= referenceDate)
            .ToList();

        // Các lô đã xuất kho.
        var shippedLots = scopedLots.Count(x =>
            x.Status == FrozenLotStatus.Shipped);

        // Các lô hết hạn.
        var expiredLots = scopedLots.Count(x =>
    x.Status == FrozenLotStatus.Expired ||
    x.ExpiryDate.Date < referenceDate);

        // Các lô có thể bán.
        var availableLots = inventoryLots.Count(x =>
            x.Status == FrozenLotStatus.Available);

        // Các lô đã được đặt trước.
        var reservedLots = inventoryLots.Count(x =>
            x.Status == FrozenLotStatus.Reserved);

        // ============================================================
        // 3. TỔNG QUAN TỒN KHO
        // ============================================================

        var totalInventoryLots = inventoryLots.Count;

        var totalInventoryWeightKg = inventoryLots
            .Sum(x => x.WeightKg);

        var totalInventoryQuantity = inventoryLots
            .Sum(x => x.Quantity);

        // ============================================================
        // 4. THỐNG KÊ THEO GRADE
        // ============================================================

        var byGrade = inventoryLots
            .GroupBy(x =>
                string.IsNullOrWhiteSpace(x.Grade)
                    ? "Unknown"
                    : x.Grade)
            .Select(group => new InventoryByGradeDto(
                Grade: group.Key,

                // Số lô của Grade
                LotCount: group.Count(),

                // Tổng số lượng cua
                Quantity: group.Sum(x => x.Quantity),

                // Tổng khối lượng
                WeightKg: group.Sum(x => x.WeightKg)
            ))
            .OrderBy(x => x.Grade)
            .ToList();

        var expiryCutoff = referenceDate.AddDays(warningDays);

        var expiryCandidates = scopedLots
    .Where(l =>
        l.Status == FrozenLotStatus.Available ||
        l.Status == FrozenLotStatus.Reserved)
    .ToList();

        var expiryAlerts = expiryCandidates
            .Where(l => l.ExpiryDate.Date <= expiryCutoff)
            .Select(l => new InventoryExpiryAlertDto(
                LotCode: l.LotCode,
                Grade: l.Grade ?? "Unknown",
                WeightKg: l.WeightKg,
                Quantity: l.Quantity,
                ExpiryDate: l.ExpiryDate,
                DaysUntilExpiry: Math.Max(
                    0,
                    (l.ExpiryDate.Date - referenceDate).Days),
                Status: l.ExpiryDate.Date < referenceDate
                    ? "expired"
                    : "expiring_soon"))
            .OrderBy(x => x.DaysUntilExpiry)
            .ToList();

        // ============================================================
        // 6. XU HƯỚNG TỒN KHO (gom theo ngày đóng băng)
        // ============================================================
        var trend = inventoryLots
            .GroupBy(l => l.FrozenDate.Date)
            .Select(g => new InventoryTrendDto(
                Date: g.Key,
                LotCount: g.Count(),
                WeightKg: g.Sum(l => l.WeightKg),
                Quantity: g.Sum(l => l.Quantity)
            ))
            .OrderBy(x => x.Date)
            .ToList();

        // ============================================================
        // 7. TRẢ KẾT QUẢ
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
}