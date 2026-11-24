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

/// <summary>Chuỗi dùng trong [Authorize(Roles = ...)]. Chỉ dùng tên role chính thức.</summary>
public static class AppRoles
{
    public const string SystemAdministrator = nameof(UserRole.SystemAdministrator);
    public const string FarmAdministrator = nameof(UserRole.FarmAdministrator);
    public const string FarmManager = nameof(UserRole.FarmManager);
    public const string FarmStaff = nameof(UserRole.FarmStaff);

    /// <summary>Mọi user đăng nhập hợp lệ.</summary>
    public const string Any =
        "SystemAdministrator,FarmAdministrator,FarmManager,FarmStaff";

    /// <summary>Cổng trang trại. Không gồm System Administrator.</summary>
    public const string FarmPortal =
        "FarmAdministrator,FarmManager,FarmStaff";

    /// <summary>Thao tác vận hành trại — Farm Administrator và role trại.</summary>
    public const string FarmWrite = FarmPortal;

    /// <summary>Quản trị cấu trúc trại — Farm Administrator.</summary>
    public const string FarmManage = "FarmAdministrator";

    /// <summary>FarmWrite + System Administrator (admin vẫn thao tác được khi cần).</summary>
    public const string FarmWriteWithPlatform = FarmWrite + "," + Platform;

    /// <summary>FarmManage + System Administrator.</summary>
    public const string FarmManageWithPlatform = FarmManage + "," + Platform;

    /// <summary>
    /// Camera, sensor, controller, AI, lịch ghi, trạng thái thiết bị,
    /// lưu trữ, backup, triển khai và bảo mật hệ thống.
    /// </summary>
    public const string Platform = "SystemAdministrator";

    public static bool IsFarmPortalRole(UserRole role) =>
        role is UserRole.FarmAdministrator or UserRole.FarmManager or UserRole.FarmStaff;
}

public static class UserRoleParsing
{
    // Do not accept numeric enum values, case variations or legacy aliases.
    public static bool TryParse(string? raw, out UserRole role)
    {
        switch (raw)
        {
            case nameof(UserRole.SystemAdministrator):
                role = UserRole.SystemAdministrator; return true;
            case nameof(UserRole.FarmAdministrator):
                role = UserRole.FarmAdministrator; return true;
            case nameof(UserRole.FarmManager):
                role = UserRole.FarmManager; return true;
            case nameof(UserRole.FarmStaff):
                role = UserRole.FarmStaff; return true;
            default:
                role = default; return false;
        }
    }

    /// <summary>Reject corrupt or unmigrated storage values instead of granting a role.</summary>
    public static UserRole FromStorage(string? raw) =>
        TryParse(raw, out var role)
            ? role
            : throw new InvalidOperationException("Invalid stored user role; administrative remediation required.");
}
