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
        Guid? farmingAreaId = null,
        CancellationToken ct = default);

    Task<ApiResponse<HarvestOverviewDto>> GetOverviewAsync(
        Guid? farmingAreaId = null,
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

    Task<ApiResponse<HarvestVoucherDetailDto>> ApproveAsync(
        Guid id, ApproveHarvestRequest request, CancellationToken ct = default);

    Task<ApiResponse<HarvestVoucherDetailDto>> RejectAsync(
        Guid id, RejectHarvestRequest request, CancellationToken ct = default);

    Task<ApiResponse<HarvestVoucherDetailDto>> ExecuteAsync(
        Guid id, ExecuteHarvestRequest request, CancellationToken ct = default);

    Task<ApiResponse<HarvestVoucherDetailDto>> RequestAdjustmentAsync(
        Guid id, RequestHarvestAdjustmentRequest request, CancellationToken ct = default);

    Task<ApiResponse<HarvestVoucherDetailDto>> ConfirmAsync(
        Guid id, ConfirmHarvestRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a harvest voucher.
    /// Only allowed when business rules permit.
    /// </summary>
    Task<ApiResponse> DeleteAsync(
        Guid id,
        CancellationToken ct = default);

    // ------------------------------------------------------------------------
    // Box ↔ Harvest link
    // ------------------------------------------------------------------------

    /// <summary>
    /// Gets all boxes referenced in a harvest voucher.
    /// </summary>
    Task<ApiResponse<IEnumerable<HarvestBoxDto>>> GetBoxesByVoucherAsync(
        Guid voucherId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all harvest vouchers that reference a specific box.
    /// </summary>
    Task<ApiResponse<IEnumerable<BoxHarvestVoucherDto>>> GetVouchersByBoxAsync(
        Guid boxId,
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
