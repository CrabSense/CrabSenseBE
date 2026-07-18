namespace CrabSenseBE.Application.DTOs.Auth;

// --- Request DTOs ---
public record LoginRequest(string Username, string Password);
public record RegisterRequest(string Username, string Email, string Password, string FullName, string Role);
public record RefreshTokenRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

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
    DateTime? LastLoginAt
);
