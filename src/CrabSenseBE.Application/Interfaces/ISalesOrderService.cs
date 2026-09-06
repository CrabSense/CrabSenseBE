using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Sales;

namespace CrabSenseBE.Application.Interfaces;

public interface ISalesOrderService
{
    Task<ApiResponse<IEnumerable<CustomerDto>>> GetCustomersAsync(CancellationToken ct = default);

    Task<ApiResponse<CustomerDto>> UpsertCustomerAsync(
        UpsertCustomerRequest request,
        CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<SalesOrderDto>>> GetOrdersAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default);

    Task<ApiResponse<SalesOrderDto>> GetOrderByIdAsync(Guid id, CancellationToken ct = default);

    Task<ApiResponse<SalesOrderDto>> CreateOrderAsync(
        CreateSalesOrderRequest request,
        CancellationToken ct = default);

    Task<ApiResponse<SalesOverviewDto>> GetOverviewAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<InventoryCrabDto>>> GetInventoryAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default);
}
