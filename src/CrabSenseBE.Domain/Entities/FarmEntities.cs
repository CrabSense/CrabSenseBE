using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Khu vực nuôi (Area) — MOD-FARM. Thuộc chủ trại (Owner).</summary>
public class FarmingArea : BaseEntity
{
    /// <summary>Chủ sở hữu khu — gắn từ JWT lúc tạo.</summary>
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public AppUser? Owner { get; set; }
    public ICollection<FarmingRow> Rows { get; set; } = new List<FarmingRow>();
    public ICollection<WaterSystem> WaterSystems { get; set; } = new List<WaterSystem>();
}

/// <summary>Hàng nuôi (Row) trong khu vực</summary>
public class FarmingRow : BaseEntity
{
    public Guid FarmingAreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public FarmingArea? FarmingArea { get; set; }
    public ICollection<Box> Boxes { get; set; } = new List<Box>();
}

/// <summary>Hộp/Khay nuôi cua — MOD-FARM</summary>
public class Box : BaseEntity
{
    public Guid FarmingRowId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Status { get; set; }
    public bool IsOccupied { get; set; } = false;

    // Navigation
    public FarmingRow? FarmingRow { get; set; }
    public ICollection<Crab> Crabs { get; set; } = new List<Crab>();
}

/// <summary>Cá thể cua — MOD-FARM. Trong hộp + thuộc lô + vụ nuôi.</summary>
public class Crab : BaseEntity
{
    public Guid BoxId { get; set; }
    public Guid CrabLotId { get; set; }
    public Guid CropBatchId { get; set; }
    public string? Tag { get; set; }
    public decimal? WeightGram { get; set; }
    public string? MoltingStage { get; set; }
    public bool IsAlive { get; set; } = true;
    public DateTime? MoltedAt { get; set; }

    // Navigation
    public Box? Box { get; set; }
    public CrabLot? CrabLot { get; set; }
    public CropBatch? CropBatch { get; set; }
}

/// <summary>Lô cua (Lot) — nhóm cua nhập vào</summary>
public class CrabLot : BaseEntity
{
    public string LotCode { get; set; } = string.Empty;
    public DateTime ImportDate { get; set; }
    public int Quantity { get; set; }
    public decimal? AverageWeightGram { get; set; }
    public string? SupplierName { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public ICollection<Crab> Crabs { get; set; } = new List<Crab>();
}

/// <summary>Đợt nuôi (CropBatch) — theo vụ</summary>
public class CropBatch : BaseEntity
{
    public string BatchCode { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Status { get; set; }
    public string? Notes { get; set; }

    public ICollection<Crab> Crabs { get; set; } = new List<Crab>();
}

/// <summary>Nhật ký vận hành — MOD-FARM</summary>
public class OperationLog : BaseEntity
{
    public Guid UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }

    // Navigation
    public AppUser? User { get; set; }
}
