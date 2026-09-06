using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Khách hàng — MOD-SALES</summary>
public class Customer : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string CustomerType { get; set; } = "retail"; // retail, wholesale, export
    public string? TaxCode { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<SalesOrder> SalesOrders { get; set; } = new List<SalesOrder>();
}

/// <summary>Bảng giá — theo loại/size</summary>
public class PriceList : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Grade { get; set; } = "M"; // S, M, L
    public string CustomerType { get; set; } = "retail";
    public decimal PricePerKg { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Đơn bán hàng — MOD-MARKET</summary>
public class SalesOrder : BaseEntity
{
    public string OrderCode { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Quotation;
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? FarmingAreaId { get; set; }
    public string? SellerName { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    // Navigation
    public Customer? Customer { get; set; }
    public ICollection<SalesOrderLine> Lines { get; set; } = new List<SalesOrderLine>();
    public ICollection<Delivery> Deliveries { get; set; } = new List<Delivery>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public Invoice? Invoice { get; set; }
}

/// <summary>Chi tiết đơn hàng</summary>
public class SalesOrderLine : BaseEntity
{
    public Guid SalesOrderId { get; set; }
    public Guid? FrozenLotId { get; set; }
    public Guid? CrabId { get; set; }
    public string? CrabCode { get; set; }
    public string? Grade { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal QuantityKg { get; set; }
    public decimal UnitPricePerKg { get; set; }
    public decimal TotalAmount { get; set; }

    // Navigation
    public SalesOrder? SalesOrder { get; set; }
    public FrozenLot? FrozenLot { get; set; }
}

/// <summary>Giao hàng — MOD-MARKET</summary>
public class Delivery : BaseEntity
{
    public Guid SalesOrderId { get; set; }
    public DateTime? PlannedDate { get; set; }
    public DateTime? ActualDate { get; set; }
    public string? Address { get; set; }
    public string? Driver { get; set; }
    public string? VehiclePlate { get; set; }
    public string Status { get; set; } = "pending";

    // Navigation
    public SalesOrder? SalesOrder { get; set; }
}

/// <summary>Thanh toán — MOD-SALES</summary>
public class Payment : BaseEntity
{
    public Guid SalesOrderId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "cash"; // cash, transfer
    public DateTime PaidAt { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;
    public string? Reference { get; set; }

    // Navigation
    public SalesOrder? SalesOrder { get; set; }
}

/// <summary>Hóa đơn — MOD-SALES</summary>
public class Invoice : BaseEntity
{
    public Guid SalesOrderId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string? Notes { get; set; }

    // Navigation
    public SalesOrder? SalesOrder { get; set; }
}

/// <summary>Cài đặt trại — MOD-SYS</summary>
public class FarmSettings : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "general";
}
