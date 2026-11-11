using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Lô cua đông lạnh — MOD-FROZEN</summary>
public class FrozenLot : BaseEntity
{
    public string LotCode { get; set; } = string.Empty;

    /// <summary>Khu sở hữu lô đông; bắt buộc với lô tạo mới.</summary>
    public Guid? FarmingAreaId { get; set; }
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
    public uint ConcurrencyVersion { get; set; }

    // Navigation
    public FarmingArea? FarmingArea { get; set; }
    public HarvestVoucher? HarvestVoucher { get; set; }
    public ICollection<FrozenStorageTransaction> StorageTransactions { get; set; }
        = new List<FrozenStorageTransaction>();
}

/// <summary>Phiếu nhập hoặc xuất kho, tách khỏi bản ghi lô đông.</summary>
public class FrozenStorageTransaction : BaseEntity
{
    public string TransactionCode { get; set; } = string.Empty;
    public Guid FrozenLotId { get; set; }
    public Guid FarmingAreaId { get; set; }
    public FrozenStorageTransactionType Type { get; set; }
    public FrozenStorageTransactionStatus Status { get; set; }
        = FrozenStorageTransactionStatus.PendingApproval;
    public int RequestedQuantity { get; set; }
    public decimal RequestedWeightKg { get; set; }
    public int? ApprovedQuantity { get; set; }
    public decimal? ApprovedWeightKg { get; set; }
    public int? ActualQuantity { get; set; }
    public decimal? ActualWeightKg { get; set; }
    public int? QuantityVariance { get; set; }
    public decimal? WeightVarianceKg { get; set; }
    public string? VarianceReason { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? PerformedByUserId { get; set; }
    public DateTime? PerformedAt { get; set; }
    public string? Notes { get; set; }
    public uint ConcurrencyVersion { get; set; }

    public FrozenLot? FrozenLot { get; set; }
    public FarmingArea? FarmingArea { get; set; }
    public ICollection<FrozenInventoryAdjustment> Adjustments { get; set; }
        = new List<FrozenInventoryAdjustment>();
}

/// <summary>Nhật ký bất biến của mỗi lần thay đổi số tồn.</summary>
public class FrozenInventoryAdjustment : BaseEntity
{
    public Guid FrozenLotId { get; set; }
    public Guid FrozenStorageTransactionId { get; set; }
    public Guid FarmingAreaId { get; set; }
    public int QuantityDelta { get; set; }
    public decimal WeightDeltaKg { get; set; }
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
    public decimal WeightBeforeKg { get; set; }
    public decimal WeightAfterKg { get; set; }
    public Guid PerformedByUserId { get; set; }
    public string? Reason { get; set; }

    public FrozenLot? FrozenLot { get; set; }
    public FrozenStorageTransaction? FrozenStorageTransaction { get; set; }
}

/// <summary>
/// Một sản phẩm cua riêng lẻ trong lô cấp đông.
/// Mỗi sản phẩm có barcode riêng và snapshot dữ liệu tại thời điểm cấp đông.
/// </summary>
public class FrozenCrabItem : BaseEntity
{
    public Guid FrozenLotId { get; set; }
    public Guid HarvestLineId { get; set; }
    public Guid CrabId { get; set; }

    // Mã được encode vào barcode Code 128.
    public string BarcodeValue { get; set; } = string.Empty;

    // Snapshot mã để tra cứu vẫn rõ ràng nếu thông tin gốc thay đổi.
    public string LotCode { get; set; } = string.Empty;
    public string CrabCode { get; set; } = string.Empty;
    public string? HarvestVoucherCode { get; set; }

    // Snapshot thông tin hiển thị trên trang sau khi quét.
    public DateTime HarvestDate { get; set; }
    public DateTime FrozenDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public decimal WeightGram { get; set; }

    /// <summary>Size cua: S, M hoặc L.</summary>
    public string Grade { get; set; } = string.Empty;

    public FrozenLot? FrozenLot { get; set; }
    public HarvestLine? HarvestLine { get; set; }
    public Crab? Crab { get; set; }
}
