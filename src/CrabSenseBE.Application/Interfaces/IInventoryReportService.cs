using CrabSenseBE.Application.DTOs.Reports;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>
/// Service xử lý các báo cáo liên quan đến tồn kho.
/// </summary>
public interface IInventoryReportService
{
    /// <summary>
    /// Lấy báo cáo tồn kho cua đông lạnh.
    /// </summary>
    Task<InventoryReportDto> GetInventoryReportAsync(
        CancellationToken cancellationToken = default);
}