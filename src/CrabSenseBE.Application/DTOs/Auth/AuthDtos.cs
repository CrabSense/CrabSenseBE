namespace CrabSenseBE.Application.DTOs.Auth;

// --- Request DTOs ---
public record LoginRequest(string Username, string Password);
/// <param name="Role">SystemAdmin | FarmOwner | Staff</param>
public record RegisterRequest(string Username, string Email, string Password, string FullName, string Role);
public record RefreshTokenRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
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
    DateTime? CreatedAt = null
);
