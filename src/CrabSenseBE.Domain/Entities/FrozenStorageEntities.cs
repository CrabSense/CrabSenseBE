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
