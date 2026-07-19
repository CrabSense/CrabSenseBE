using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>
/// Mã QR dùng cho box tại trại và truy xuất lô cấp đông hoặc phiếu thu hoạch.
/// Database: qr_code với entity_type và entity_id.
/// </summary>
public class QrCode : BaseEntity
{
    /// <summary>
    /// Giá trị được in trên tem hoặc encode trong QR.
    /// Ví dụ: BOX-A01-7F3A.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Loại đối tượng: box, frozen_lot hoặc harvest_voucher.
    /// </summary>
    public string EntityType { get; set; } = "box";

    public Guid? BoxId { get; set; }

    public Guid? FrozenLotId { get; set; }

    public Guid? HarvestVoucherId { get; set; }

    /// <summary>
    /// Dữ liệu JSON phụ như deep-link hoặc metadata.
    /// </summary>
    public string? Payload { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public int ScanCount { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public Box? Box { get; set; }

    public FrozenLot? FrozenLot { get; set; }

    public ICollection<TraceabilityLink> TraceabilityLinks { get; set; }
        = new List<TraceabilityLink>();
}

/// <summary>Liên kết dữ liệu phục vụ truy xuất nguồn gốc công khai.</summary>
public class TraceabilityLink : BaseEntity
{
    public Guid QrCodeId { get; set; }

    /// <summary>Loại liên kết: harvest, farm hoặc frozen.</summary>
    public string LinkType { get; set; } = string.Empty;

    public Guid LinkedEntityId { get; set; }

    public string LinkedEntityType { get; set; } = string.Empty;

    // Navigation
    public QrCode? QrCode { get; set; }
}
