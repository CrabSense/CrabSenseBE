namespace CrabSenseBE.Application.DTOs.Sales;

public record CustomerDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    string CustomerType,
    bool IsActive);

public record UpsertCustomerRequest(
    string Name,
    string? Phone = null,
    string? Email = null,
    string? Address = null,
    string? CustomerType = null);

public record SalesOrderLineDto(
    Guid Id,
    Guid? CrabId,
    string? CrabCode,
    string? Grade,
    int Quantity,
    decimal WeightGram,
    decimal QuantityKg,
    decimal UnitPricePerKg,
    decimal TotalAmount);

public record SalesOrderDto(
    Guid Id,
    string OrderCode,
    DateTime OrderDate,
    string Status,
    string PaymentStatus,
    decimal TotalAmount,
    int CrabCount,
    Guid CustomerId,
    string CustomerName,
    string? CustomerPhone,
    string? SellerName,
    Guid? FarmingAreaId,
    string? Notes,
    IReadOnlyList<SalesOrderLineDto> Lines);

public record CreateSalesOrderRequest(
    DateTime? OrderDate,
    string CustomerName,
    string? CustomerPhone,
    string? PaymentStatus,
    string? SellerName,
    Guid? FarmingAreaId,
    string? Notes,
    IEnumerable<CreateSalesOrderLineRequest> Lines);

public record CreateSalesOrderLineRequest(
    Guid CrabId,
    decimal? UnitPricePerKg,
    decimal? WeightGram = null,
    string? Grade = null);

public record SalesOverviewDto(
    decimal RevenueToday,
    int SoldToday,
    int InventoryCount,
    decimal InventoryWeightKg);

public record InventoryCrabDto(
    Guid Id,
    string Code,
    string? BoxCode,
    decimal? WeightGram,
    string? Grade,
    string? Condition,
    DateTime? HarvestedAt);
