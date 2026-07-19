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

/// <summary>
/// Mã QR — dùng cho box tại trại (NV quét điện thoại) và truy xuất lô đông/harvest.
/// DB: qr_code (entity_type + entity_id).
/// </summary>
public class QrCode : BaseEntity
{
    /// <summary>Giá trị in trên tem / encode trong QR (vd: BOX-A01-7F3A).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>box | frozen_lot | harvest_voucher</summary>
    public string EntityType { get; set; } = "box";

    public Guid? BoxId { get; set; }
    public Guid? FrozenLotId { get; set; }
    public Guid? HarvestVoucherId { get; set; }

    /// <summary>JSON phụ (URL deep-link App, meta…).</summary>
    public string? Payload { get; set; }

    public DateTime? ExpiresAt { get; set; }
    public int ScanCount { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    public Box? Box { get; set; }
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
