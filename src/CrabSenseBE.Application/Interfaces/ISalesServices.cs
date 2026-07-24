using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Sales;

namespace CrabSenseBE.Application.Interfaces;

public interface ISalesService
{
    Task<ApiResponse<SaleDto>> CreateAsync(CreateSaleRequest req, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<SaleDto>>> GetHistoryAsync(
        Guid? farmId = null,
        string? buyerName = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? paymentMethod = null,
        string? paymentStatus = null,
        int page = 1,
        int limit = 50,
        CancellationToken ct = default);
    Task<ApiResponse<SalesSummaryDto>> GetSummaryAsync(
        DateTime startDate, DateTime endDate, Guid? farmId = null, CancellationToken ct = default);
    Task<ApiResponse<SaleDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<object>> GetAvailableInventoryAsync(Guid? farmId = null, CancellationToken ct = default);
}

public interface IBoxCameraService
{
    Task<ApiResponse<BoxCameraDto>> GetCameraForBoxAsync(Guid boxId, CancellationToken ct = default);
}
