using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Kiosk tại một khu nuôi. IP không phải định danh.</summary>
public class FarmKiosk : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public Guid FarmingAreaId { get; set; }
    public string? Name { get; set; }

    /// <summary>Created | Provisioning | Online | Offline | Revoked</summary>
    public string Status { get; set; } = "Created";

    public DateTime? LastSeenAt { get; set; }
    public string? LanIp { get; set; }

    public FarmingArea? FarmingArea { get; set; }
}

/// <summary>Mã cấp phát một lần, hết hạn. Không dùng làm token sau khi Kiosk đã nhận secret.</summary>
public class KioskProvisioningCode : BaseEntity
{
    public Guid KioskId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public FarmKiosk? Kiosk { get; set; }
}

/// <summary>Hash secret riêng của từng Kiosk.</summary>
public class KioskCredential : BaseEntity
{
    public Guid KioskId { get; set; }
    public string SecretHash { get; set; } = string.Empty;
    public DateTime? RevokedAt { get; set; }

    public FarmKiosk? Kiosk { get; set; }
}
