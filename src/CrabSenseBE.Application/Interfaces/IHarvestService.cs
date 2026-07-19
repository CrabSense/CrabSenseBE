using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>
/// Harvest voucher application service.
///
/// Responsibilities:
/// - Harvest voucher CRUD
/// - Harvest status management
/// - Harvest production statistics
/// - Softshell success rate calculation
/// </summary>
public interface IHarvestService
{
    // ------------------------------------------------------------------------
    // Harvest Voucher
    // ------------------------------------------------------------------------

    /// <summary>
    /// Gets all harvest vouchers.
    /// </summary>
    Task<ApiResponse<IEnumerable<HarvestVoucherDto>>> GetAllAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Gets harvest voucher detail.
    /// </summary>
    Task<ApiResponse<HarvestVoucherDetailDto>> GetByIdAsync(
        Guid id,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a harvest voucher.
    /// </summary>
    Task<ApiResponse<HarvestVoucherDetailDto>> CreateAsync(
        CreateHarvestVoucherRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Updates harvest voucher status.
    /// </summary>
    Task<ApiResponse<HarvestVoucherDetailDto>> UpdateStatusAsync(
        Guid id,
        UpdateHarvestStatusRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a harvest voucher.
    /// Only allowed when business rules permit.
    /// </summary>
    Task<ApiResponse> DeleteAsync(
        Guid id,
        CancellationToken ct = default);

    // ------------------------------------------------------------------------
    // Statistics
    // ------------------------------------------------------------------------

    /// <summary>
    /// Gets harvest statistics by date range.
    /// Period:
    /// day | week | month
    /// </summary>
    Task<ApiResponse<HarvestStatisticsDto>> GetStatisticsAsync(
        DateTime from,
        DateTime to,
        string period,
        CancellationToken ct = default);
}
