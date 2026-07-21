namespace CrabSenseBE.Domain.Enums;

/// <summary>
/// 3 role cố định:
/// SystemAdmin = admin hệ thống;
/// FarmOwner = chủ trại;
/// Staff = nhân viên.
/// </summary>
public enum UserRole
{
    SystemAdmin = 0,
    FarmOwner = 1,
    Staff = 2
}

/// <summary>Chuỗi dùng trong [Authorize(Roles = ...)].</summary>
public static class AppRoles
{
    public const string SystemAdmin = nameof(UserRole.SystemAdmin);
    public const string FarmOwner = nameof(UserRole.FarmOwner);
    public const string Staff = nameof(UserRole.Staff);

    /// <summary>Mọi user đăng nhập (3 role).</summary>
    public const string Any = "SystemAdmin,FarmOwner,Staff";

    /// <summary>Thao tác nuôi hằng ngày (Chủ trại + NV; admin hệ thống hỗ trợ).</summary>
    public const string FarmWrite = "SystemAdmin,FarmOwner,Staff";

    /// <summary>Quản trị / xóa cấu trúc trại (Admin hệ thống + Chủ trại).</summary>
    public const string FarmManage = "SystemAdmin,FarmOwner";

    /// <summary>Chỉ admin hệ thống (IoT đăng ký thiết bị, kênh thông báo,...).</summary>
    public const string Platform = "SystemAdmin";
}

public enum OrderStatus
{
    Quotation,
    Confirmed,
    Packing,
    Shipping,
    Completed,
    Cancelled
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Overdue,
    Cancelled
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum AlertStatus
{
    Active,
    Acknowledged,
    Resolved
}

public enum DeviceStatus
{
    Online,
    Offline,
    Maintenance,
    Error
}

public enum FrozenLotStatus
{
    Available,
    Reserved,
    Shipped,
    Expired
}

public enum HarvestStatus
{
    Planned,
    InProgress,
    Completed,
    Cancelled
}

/// <summary>
/// Nguyên nhân cua chết.
/// Dùng cho báo cáo tỷ lệ sống và phân tích nguyên nhân tử vong.
/// </summary>
public enum MortalityCause
{
    /// <summary>Chưa xác định được nguyên nhân.</summary>
    Unknown = 0,

    /// <summary>Bệnh.</summary>
    Disease=1,
    /// <summary>Chất lượng nước.</summary>
    WaterQuality=2,

    /// <summary>Ăn thịt lẫn nhau.</summary>
    Cannibalism=3,

    /// <summary>Vấn đề về nhiệt độ.</summary>
    Temperature=4,

    /// <summary>Vấn đề liên quan đến cho ăn.</summary>
    Feeding=5,

    /// <summary>Chết do quá trình bắt, vận chuyển hoặc xử lý.</summary>
    Handling=6,


    /// <summary>Nguyên nhân khác.</summary>
    Other=7
}

/// <summary>Allowed Box.Status values for farming tracking.</summary>
public static class BoxStatuses
{
    public const string Empty = "empty";
    public const string Active = "active";
    public const string Molting = "molting";
    public const string Quarantine = "quarantine";
    public const string Maintenance = "maintenance";
    public const string Harvested = "harvested";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Empty, Active, Molting, Quarantine, Maintenance, Harvested
    };

    public static bool IsValid(string? status) =>
        !string.IsNullOrWhiteSpace(status) && All.Contains(status.Trim());

    public static string Normalize(string status) => status.Trim().ToLowerInvariant();
}
