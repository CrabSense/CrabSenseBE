using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Lô cua đông lạnh — MOD-FROZEN</summary>
public class FrozenLot : BaseEntity
{
    public string LotCode { get; set; } = string.Empty;

    public Guid? HarvestVoucherId { get; set; }

    public DateTime FrozenDate { get; set; }

    public DateTime ExpiryDate { get; set; }

    public decimal WeightKg { get; set; }

    /// <summary>Phân loại kích cỡ: S, M, L.</summary>
    public string? Grade { get; set; }

    public int Quantity { get; set; }

    public FrozenLotStatus Status { get; set; }
        = FrozenLotStatus.Available;

    public string? StorageLocation { get; set; }

    // Navigation
    public HarvestVoucher? HarvestVoucher { get; set; }
}
