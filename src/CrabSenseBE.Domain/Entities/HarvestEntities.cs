using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Phiếu thu hoạch cua lột — MOD-HARVEST</summary>
public class HarvestVoucher : BaseEntity
{
    public string VoucherCode { get; set; } = string.Empty;
    public Guid? CropBatchId { get; set; }
    public DateTime HarvestDate { get; set; }
    public HarvestStatus Status { get; set; } = HarvestStatus.Planned;
    public int TotalQuantity { get; set; }
    public decimal TotalWeightKg { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedBy { get; set; }

    // Navigation
    public ICollection<HarvestLine> Lines { get; set; } = new List<HarvestLine>();
}

/// <summary>Chi tiết dòng thu hoạch</summary>
public class HarvestLine : BaseEntity
{
    public Guid HarvestVoucherId { get; set; }
    public Guid? CrabId { get; set; }
    public decimal WeightGram { get; set; }
    public string? Grade { get; set; } // S, M, L
    public bool IsSoftshell { get; set; } = true;
    public string? Notes { get; set; }

    // Navigation
    public HarvestVoucher? HarvestVoucher { get; set; }
    public Crab? Crab { get; set; }
}

/// <summary>Lô cua đông lạnh — MOD-FROZEN</summary>
public class FrozenLot : BaseEntity
{
    public string LotCode { get; set; } = string.Empty;
    public Guid? HarvestVoucherId { get; set; }
    public DateTime FrozenDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public decimal WeightKg { get; set; }
    public string? Grade { get; set; }
    public int Quantity { get; set; }
    public FrozenLotStatus Status { get; set; } = FrozenLotStatus.Available;
    public string? StorageLocation { get; set; }

    // Navigation
    public HarvestVoucher? HarvestVoucher { get; set; }
}

/// <summary>Mã QR truy xuất nguồn gốc — MOD-TRACE</summary>
public class QrCode : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public Guid? FrozenLotId { get; set; }
    public Guid? HarvestVoucherId { get; set; }
    public string? Payload { get; set; } // JSON blob
    public DateTime? ExpiresAt { get; set; }
    public int ScanCount { get; set; } = 0;

    // Navigation
    public FrozenLot? FrozenLot { get; set; }
    public ICollection<TraceabilityLink> TraceabilityLinks { get; set; } = new List<TraceabilityLink>();
}

/// <summary>Liên kết truy xuất — link to public lookup</summary>
public class TraceabilityLink : BaseEntity
{
    public Guid QrCodeId { get; set; }
    public string LinkType { get; set; } = string.Empty; // harvest, farm, frozen
    public Guid LinkedEntityId { get; set; }
    public string LinkedEntityType { get; set; } = string.Empty;

    // Navigation
    public QrCode? QrCode { get; set; }
}
