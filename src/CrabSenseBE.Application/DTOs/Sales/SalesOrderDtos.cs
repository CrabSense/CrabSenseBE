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
    string? CrabType,
    string? Grade,
    int Quantity,
    decimal WeightGram,
    decimal QuantityKg,
    decimal UnitPricePerKg,
    decimal TotalAmount,
    string? HarvestSlipCode = null,
    string? BoxCode = null,
    string? LotCode = null);

public record SalesOrderDto(
    Guid Id,
    string OrderCode,
    DateTime OrderDate,
    string Status,
    string PaymentStatus,
    string? PaymentMethod,
    string? DeliveryStatus,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal ShippingFee,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal TotalWeightKg,
    int CrabCount,
    Guid CustomerId,
    string CustomerName,
    string? CustomerPhone,
    string? CustomerAddress,
    string? SellerName,
    Guid? FarmingAreaId,
    string? Notes,
    IReadOnlyList<SalesOrderLineDto> Lines,
    string? CustomerType = null);

public record CreateSalesOrderRequest(
    DateTime? OrderDate,
    string CustomerName,
    string? CustomerPhone,
    string? CustomerAddress,
    string? PaymentStatus,
    string? PaymentMethod,
    string? OrderStatus,
    string? SellerName,
    Guid? FarmingAreaId,
    string? Notes,
    decimal? DiscountAmount,
    decimal? ShippingFee,
    decimal? PaidAmount,
    string? DeliveryStatus,
    IEnumerable<CreateSalesOrderLineRequest>? Lines,
    string? CustomerType = null);

public record CreateSalesOrderLineRequest(
    Guid CrabId,
    decimal? UnitPricePerKg,
    decimal? WeightGram = null,
    string? Grade = null);

public record SalesOverviewDto(
    decimal RevenueToday,
    int SoldToday,
    int InventoryCount,
    decimal InventoryWeightKg,
    int OrdersToday = 0,
    decimal UnpaidAmount = 0,
    decimal RevenueChangePercent = 0,
    int SoldChange = 0,
    int UnpaidOrdersChange = 0);

public record InventoryCrabDto(
    Guid Id,
    string Code,
    string? BoxCode,
    decimal? WeightGram,
    string? Grade,
    string? Condition,
    string? CrabType,
    DateTime? HarvestedAt,
    string? HarvestSlipCode = null,
    string? LotCode = null,
    string SaleEligibility = "READY",
    bool IsSoftshell = false);
