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
        // Lấy tất cả các lô đông lạnh từ database.
        var lots = await _uow.FrozenLots.GetAllAsync(
            cancellationToken);

        // ============================================================
        // 1. PHÂN LOẠI THEO TRẠNG THÁI
        // ============================================================

        // Có thể bán hoặc đang được giữ chỗ.
        // Đây mới là tồn kho thực tế.
        var inventoryLots = lots
            .Where(x =>
                x.Status == FrozenLotStatus.Available ||
                x.Status == FrozenLotStatus.Reserved)
            .ToList();

        // Các lô đã xuất kho.
        var shippedLots = lots.Count(x =>
            x.Status == FrozenLotStatus.Shipped);

        // Các lô hết hạn.
        var expiredLots = lots.Count(x =>
            x.Status == FrozenLotStatus.Expired);

        // Các lô có thể bán.
        var availableLots = lots.Count(x =>
            x.Status == FrozenLotStatus.Available);

        // Các lô đã được đặt trước.
        var reservedLots = lots.Count(x =>
            x.Status == FrozenLotStatus.Reserved);

        // ============================================================
        // 2. TỔNG QUAN TỒN KHO
        // ============================================================

        var totalInventoryLots = inventoryLots.Count;

        var totalInventoryWeightKg = inventoryLots
            .Sum(x => x.WeightKg);

        var totalInventoryQuantity = inventoryLots
            .Sum(x => x.Quantity);

        // ============================================================
        // 3. THỐNG KÊ THEO GRADE
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

        // ============================================================
        // 4. TRẢ KẾT QUẢ
        // ============================================================

        return new InventoryReportDto(
            TotalInventoryLots: totalInventoryLots,
            TotalInventoryWeightKg: totalInventoryWeightKg,
            TotalInventoryQuantity: totalInventoryQuantity,

            AvailableLots: availableLots,
            ReservedLots: reservedLots,
            ShippedLots: shippedLots,
            ExpiredLots: expiredLots,

            ByGrade: byGrade
        );
    }
}