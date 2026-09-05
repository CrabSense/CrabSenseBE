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

/// <summary>Trạng thái hoạt động của trại / khu nuôi (FarmingArea).</summary>
public enum FarmStatus
{
    /// <summary>Đang hoạt động.</summary>
    Active = 0,
    /// <summary>Tạm ngưng.</summary>
    Suspended = 1,
    /// <summary>Ngừng hoạt động.</summary>
    Closed = 2
}

/// <summary>Chuỗi Status trên API tạo/sửa trại.</summary>
public static class FarmStatuses
{
    public const string Active = nameof(FarmStatus.Active);
    public const string Suspended = nameof(FarmStatus.Suspended);
    public const string Closed = nameof(FarmStatus.Closed);
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

public enum CrabStatus
{
    /// <summary>Cua đang sống bình thường.</summary>
    Alive = 0,
    /// <summary>Cua đang trong quá trình lột xác.</summary>
    Molting = 1,
    /// <summary>Cua đã chết.</summary>
    Dead = 2,
    /// <summary>Cua đang được cách ly.</summary>
    Quarantined = 3,
    /// <summary>Cua đã được thu hoạch.</summary>
    Harvested = 4,
    /// <summary>Không xác định được tình trạng/vị trí cua.</summary>
    Missing = 5
}

/// <summary>Trạng thái Owner trên card cua — 1 trường chính.</summary>
public enum CrabCondition
{
    /// <summary>🟢 Bình thường</summary>
    Normal = 0,
    /// <summary>🟡 Sắp lột</summary>
    Premolt = 1,
    /// <summary>🔵 Đang lột</summary>
    Molting = 2,
    /// <summary>🟣 Cua mềm / cua lột</summary>
    Softshell = 3,
    /// <summary>🔴 Có vấn đề</summary>
    Problem = 4,
    /// <summary>⚫ Đã chết</summary>
    Dead = 5,
    /// <summary>⚪ Đã thu hoạch</summary>
    Harvested = 6
}

public enum CrabGender
{
    Unknown = 0,
    Male = 1,
    Female = 2
}

public static class CrabConditions
{
    public static CrabCondition FromMoltingAndStatus(string? moltingStage, CrabStatus status)
    {
        if (status == CrabStatus.Dead) return CrabCondition.Dead;
        if (status == CrabStatus.Harvested) return CrabCondition.Harvested;
        if (status is CrabStatus.Quarantined or CrabStatus.Missing) return CrabCondition.Problem;
        if (status == CrabStatus.Molting) return CrabCondition.Molting;

        var stage = (moltingStage ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);
        return stage switch
        {
            "premolt" or "pre" => CrabCondition.Premolt,
            "molting" or "molt" => CrabCondition.Molting,
            "softshell" or "soft" or "postmolt" or "post" => CrabCondition.Softshell,
            _ => CrabCondition.Normal
        };
    }

    public static CrabStatus ToLifecycle(CrabCondition condition) => condition switch
    {
        CrabCondition.Dead => CrabStatus.Dead,
        CrabCondition.Harvested => CrabStatus.Harvested,
        CrabCondition.Problem => CrabStatus.Quarantined,
        CrabCondition.Molting => CrabStatus.Molting,
        _ => CrabStatus.Alive
    };

    public static string ToApi(CrabCondition condition) => condition switch
    {
        CrabCondition.Premolt => "premolt",
        CrabCondition.Molting => "molting",
        CrabCondition.Softshell => "softshell",
        CrabCondition.Problem => "problem",
        CrabCondition.Dead => "dead",
        CrabCondition.Harvested => "harvested",
        _ => "normal"
    };

    public static CrabCondition Parse(string? raw, CrabCondition fallback = CrabCondition.Normal)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant()
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);
        return key switch
        {
            "normal" or "binhthuong" or "hard" or "hardshell" => CrabCondition.Normal,
            "premolt" or "pre" or "saplot" => CrabCondition.Premolt,
            "molting" or "molt" or "danglot" => CrabCondition.Molting,
            "softshell" or "soft" or "postmolt" or "post" or "cualotmem" => CrabCondition.Softshell,
            "problem" or "alert" or "quarantined" or "missing" or "covande" => CrabCondition.Problem,
            "dead" or "deceased" or "chet" => CrabCondition.Dead,
            "harvested" or "harvest" or "dathuhoach" => CrabCondition.Harvested,
            _ => fallback
        };
    }

    public static CrabGender ParseGender(string? raw)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant();
        return key switch
        {
            "male" or "m" or "duc" or "đực" => CrabGender.Male,
            "female" or "f" or "cai" or "cái" => CrabGender.Female,
            _ => CrabGender.Unknown
        };
    }
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
