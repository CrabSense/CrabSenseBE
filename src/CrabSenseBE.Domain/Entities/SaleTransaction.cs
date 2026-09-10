using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Mobile quick-sale transaction.</summary>
public class SaleTransaction : BaseEntity
{
    public string BuyerName { get; set; } = string.Empty;
    public string? BuyerContact { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = "CASH";
    public string PaymentStatus { get; set; } = "COMPLETED";
    public DateTime SaleDate { get; set; } = DateTime.UtcNow;
    public Guid? FarmingAreaId { get; set; }
    public Guid? BoxId { get; set; }
    public Guid OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
