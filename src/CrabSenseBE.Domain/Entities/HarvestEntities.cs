using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Phiếu thu hoạch cua lột — MOD-HARVEST</summary>
public class HarvestVoucher : BaseEntity
{
    public string VoucherCode { get; set; } = string.Empty;
    // public Guid? CropBatchId { get; set; }
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
    public Guid? BoxId { get; set; }
    public decimal WeightGram { get; set; }
    public string? Grade { get; set; } // S, M, L
    public bool IsSoftshell { get; set; } = true;
    public string? Notes { get; set; }
    
    // Navigation
    public HarvestVoucher? HarvestVoucher { get; set; }
    public Crab? Crab { get; set; }
    public Box? Box { get; set; }
}

