namespace CrabSenseBE.Domain.Enums;

/// <summary>
/// 4 role cố định (chuỗi API = tên enum):
/// SystemAdministrator, FarmAdministrator, FarmManager, FarmStaff.
/// </summary>
public enum UserRole
{
    SystemAdministrator = 0,
    FarmAdministrator = 1,
    FarmManager = 2,
    FarmStaff = 3,
}

/// <summary>Chuỗi dùng trong [Authorize(Roles = ...)]. Gồm alias legacy trong thời gian migrate DB.</summary>
public static class AppRoles
{
    public const string SystemAdministrator = nameof(UserRole.SystemAdministrator);
    public const string FarmAdministrator = nameof(UserRole.FarmAdministrator);
    public const string FarmManager = nameof(UserRole.FarmManager);
    public const string FarmStaff = nameof(UserRole.FarmStaff);

    /// <summary>Mọi user đăng nhập hợp lệ.</summary>
    public const string Any =
        "SystemAdministrator,FarmAdministrator,FarmManager,FarmStaff,SystemAdmin,FarmOwner,Staff";

    /// <summary>Cổng trang trại. Không gồm System Administrator.</summary>
    public const string FarmPortal =
        "FarmAdministrator,FarmManager,FarmStaff,FarmOwner,Staff";

    /// <summary>Thao tác vận hành trại — Farm Administrator và role trại.</summary>
    public const string FarmWrite = FarmPortal;

    /// <summary>Quản trị cấu trúc trại — Farm Administrator.</summary>
    public const string FarmManage = "FarmAdministrator,FarmOwner";

    /// <summary>
    /// Camera, sensor, controller, AI, lịch ghi, trạng thái thiết bị,
    /// lưu trữ, backup, triển khai và bảo mật hệ thống.
    /// </summary>
    public const string Platform = "SystemAdministrator,SystemAdmin";

    public static bool IsFarmPortalRole(UserRole role) =>
        role is UserRole.FarmAdministrator or UserRole.FarmManager or UserRole.FarmStaff;
}

public static class UserRoleParsing
{
    public static bool TryParse(string? raw, out UserRole role)
    {
        role = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var key = raw.Trim();
        if (key.Equals("System Administrator", StringComparison.OrdinalIgnoreCase)
            || key.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase))
        {
            role = UserRole.SystemAdministrator;
            return true;
        }

        if (key.Equals("Farm Administrator", StringComparison.OrdinalIgnoreCase)
            || key.Equals("FarmOwner", StringComparison.OrdinalIgnoreCase))
        {
            role = UserRole.FarmAdministrator;
            return true;
        }

        if (key.Equals("Farm Manager", StringComparison.OrdinalIgnoreCase))
        {
            role = UserRole.FarmManager;
            return true;
        }

        if (key.Equals("Farm Staff", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Staff", StringComparison.OrdinalIgnoreCase))
        {
            role = UserRole.FarmStaff;
            return true;
        }

        return Enum.TryParse(key, ignoreCase: true, out role) && Enum.IsDefined(role);
    }

    /// <summary>Đọc cột Role từ Postgres (kể cả chuỗi legacy trước khi bootstrap remap).</summary>
    public static UserRole FromStorage(string? raw) =>
        TryParse(raw, out var role) ? role : UserRole.FarmStaff;
}
