namespace CrabSenseBE.Application.DTOs.Sales;

// --- Customer ---
public record CustomerDto(Guid Id, string Name, string? Phone, string? Email, string CustomerType, bool IsActive);
public record CreateCustomerRequest(string Name, string? Phone, string? Email, string? Address, string CustomerType, string? TaxCode);
public record UpdateCustomerRequest(string Name, string? Phone, string? Email, string? Address, bool IsActive);

// --- PriceList ---
public record PriceListDto(Guid Id, string Name, string Grade, string CustomerType, decimal PricePerKg, DateTime EffectiveFrom, DateTime? EffectiveTo, bool IsActive);
public record CreatePriceListRequest(string Name, string Grade, string CustomerType, decimal PricePerKg, DateTime EffectiveFrom, DateTime? EffectiveTo);

// --- SalesOrder ---
public record SalesOrderDto(
    Guid Id, string OrderCode, Guid CustomerId, string CustomerName,
    DateTime OrderDate, string Status, decimal TotalAmount, string? Notes
);
public record CreateSalesOrderRequest(
    Guid CustomerId, DateTime OrderDate, string? Notes,
    IEnumerable<SalesOrderLineRequest> Lines
);
public record SalesOrderLineRequest(Guid? FrozenLotId, string? Grade, decimal QuantityKg, decimal UnitPricePerKg);
public record UpdateOrderStatusRequest(string Status);

// --- Payment ---
public record PaymentDto(Guid Id, Guid SalesOrderId, decimal Amount, string Method, DateTime PaidAt, string Status, string? Reference);
public record CreatePaymentRequest(Guid SalesOrderId, decimal Amount, string Method, DateTime PaidAt, string? Reference);

// --- Invoice ---
public record InvoiceDto(Guid Id, Guid SalesOrderId, string InvoiceNumber, DateTime IssuedAt, decimal TotalAmount, string PaymentStatus);
