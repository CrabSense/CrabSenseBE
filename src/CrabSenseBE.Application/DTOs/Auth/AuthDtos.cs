namespace CrabSenseBE.Application.DTOs.Auth;

// --- Request DTOs ---
public record LoginRequest(string Username, string Password);
/// <param name="Role">FarmAdministrator, FarmManager hoặc FarmStaff; không tạo SystemAdministrator qua request này.</param>
/// <param name="FarmingAreaId">Bắt buộc với FarmManager/FarmStaff; người tạo chọn một khu/trang trại đã có.</param>
/// <param name="FarmingAreaIds">Bắt buộc với FarmAdministrator; danh sách khu/trang trại được giao quản lý.</param>
public record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string FullName,
    string Role,
    Guid? FarmingAreaId = null,
    IReadOnlyCollection<Guid>? FarmingAreaIds = null);
public record RefreshTokenRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record SetUserActiveRequest(bool IsActive);
public record ResetUserPasswordRequest(string NewPassword);
public record UpdateProfileRequest(
    string? FullName,
    string? Email,
    string? Phone,
    string? EmployeeId,
    string? AvatarUrl
);
public record NotificationPreferencesDto(
    bool WarningsEnabled = true,
    bool TaskRemindersEnabled = true,
    bool SystemUpdatesEnabled = true,
    bool SoundEnabled = true,
    bool VibrationEnabled = true,
    bool LedIndicatorEnabled = true
)
{
    public bool CriticalAlertsEnabled => true;
}

// --- Response DTOs ---
public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User
);

public record UserDto(
    Guid Id,
    string Username,
    string Email,
    string FullName,
    string Role,
    bool IsActive,
    DateTime? LastLoginAt,
    string? Phone = null,
    string? EmployeeId = null,
    string? AvatarUrl = null,
    DateTime? CreatedAt = null,
    /// <summary>ID khu của Manager/Staff; null với role không gắn một khu trực tiếp.</summary>
    Guid? FarmingAreaId = null,
    /// <summary>Các khu Farm Administrator được giao quản lý.</summary>
    IReadOnlyCollection<Guid>? FarmingAreaIds = null
);


/// <summary>
/// Admin updates employee account information.
/// </summary>
public record UpdateAdminUserRequest(
    string FullName,
    string Email,
    string? Phone,
    string? EmployeeId
);

/// <summary>
/// Admin changes employee role.
/// </summary>
public record ChangeUserRoleRequest(
    string Role
);
