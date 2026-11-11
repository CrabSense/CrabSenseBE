namespace CrabSenseBE.Application.DTOs.Auth;

// --- Request DTOs ---
public record LoginRequest(string Username, string Password);
/// <param name="Role">FarmAdministrator, FarmManager hoặc FarmStaff; không tạo SystemAdministrator qua request này.</param>
/// <param name="FarmingAreaId">Bắt buộc với FarmStaff; tương thích client cũ khi tạo Manager một khu.</param>
/// <param name="FarmingAreaIds">Danh sách khu của FarmAdministrator hoặc FarmManager.</param>
/// <param name="ManagerUserId">Bắt buộc với FarmStaff; manager đang quản lý đúng khu đó.</param>
public record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string FullName,
    string Role,
    Guid? FarmingAreaId = null,
    IReadOnlyCollection<Guid>? FarmingAreaIds = null,
    Guid? ManagerUserId = null);
public record RefreshTokenRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record SetUserActiveRequest(bool IsActive);
public record ResetUserPasswordRequest(string NewPassword);
public record UpdateProfileRequest(
    string? FullName,
    string? Email,
    string? Phone,
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
    string? UserCode = null,
    string? AvatarUrl = null,
    DateTime? CreatedAt = null,
    /// <summary>ID khu của Staff hoặc Manager chỉ có đúng một khu; Manager nhiều khu nhận null.</summary>
    Guid? FarmingAreaId = null,
    /// <summary>Các khu Farm Administrator hoặc Farm Manager được giao quản lý.</summary>
    IReadOnlyCollection<Guid>? FarmingAreaIds = null,
    /// <summary>Manager của Farm Staff.</summary>
    Guid? ManagerUserId = null
);


/// <summary>
/// Admin updates employee account information.
/// </summary>
public record UpdateAdminUserRequest(
    string FullName,
    string Email,
    string? Phone
);

/// <summary>
/// Admin changes employee role.
/// </summary>
public record ChangeUserRoleRequest(
    string Role,
    /// <summary>Bắt buộc khi đổi sang FarmStaff: manager cùng khu.</summary>
    Guid? ManagerUserId = null
);

public record UpdateManagerFarmingAreasRequest(
    IReadOnlyCollection<Guid> FarmingAreaIds);
