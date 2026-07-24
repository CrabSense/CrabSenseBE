namespace CrabSenseBE.Application.DTOs.Sales;

public record SaleDto(
    Guid Id,
    string BuyerName,
    string? BuyerContact,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalAmount,
    string PaymentMethod,
    string PaymentStatus,
    DateTime SaleDate,
    Guid? FarmId,
    Guid? BoxId,
    Guid OperatorId,
    string OperatorName,
    string? Notes,
    DateTime CreatedAt,
    bool IsSynced = true);

public record CreateSaleRequest(
    string BuyerName,
    decimal Quantity,
    decimal UnitPrice,
    string PaymentMethod = "CASH",
    string? PaymentStatus = "COMPLETED",
    string? BuyerContact = null,
    Guid? FarmId = null,
    Guid? FarmingAreaId = null,
    Guid? BoxId = null,
    Guid? OperatorId = null,
    string? OperatorName = null,
    string? Notes = null,
    DateTime? SaleDate = null,
    decimal? TotalAmount = null);

public record SalesSummaryDto(
    decimal TotalRevenue,
    decimal TotalQuantity,
    int TransactionCount,
    DateTime StartDate,
    DateTime EndDate);

public record BoxCameraDto(
    Guid BoxId,
    Guid? DeviceId,
    string DeviceCode,
    string Status,
    string? StreamUrl,
    string? SnapshotUrl,
    DateTime? LastSeenAt,
    string Message);
