using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using BCrypt.Net;

namespace CrabSenseBE.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _uow;
    private readonly IJwtService _jwt;

    public AuthService(IUnitOfWork uow, IJwtService jwt)
    {
        _uow = uow;
        _jwt = jwt;
    }

    public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _uow.Users.FirstOrDefaultAsync(
            u => u.Username == request.Username && u.IsActive, ct);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw AppException.Unauthorized("Invalid username or password.");

        var accessToken = _jwt.GenerateAccessToken(user);
        var refreshToken = _jwt.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        user.LastLoginAt = DateTime.UtcNow;
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<LoginResponse>.Ok(new LoginResponse(
            accessToken,
            refreshToken,
            DateTime.UtcNow.AddHours(1),
            MapToDto(user)
        ));
    }

    public async Task<ApiResponse<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var user = await _uow.Users.FirstOrDefaultAsync(
            u => u.RefreshToken == request.RefreshToken
                 && u.RefreshTokenExpiry > DateTime.UtcNow
                 && u.IsActive, ct);

        if (user is null)
            throw AppException.Unauthorized("Invalid or expired refresh token.");

        var accessToken = _jwt.GenerateAccessToken(user);
        var newRefresh = _jwt.GenerateRefreshToken();
        user.RefreshToken = newRefresh;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<LoginResponse>.Ok(new LoginResponse(
            accessToken, newRefresh, DateTime.UtcNow.AddHours(1), MapToDto(user)));
    }

    public async Task<ApiResponse> LogoutAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(userId, ct)
            ?? throw AppException.NotFound("User");
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Logged out successfully.");
    }

    public async Task<ApiResponse<UserDto>> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (await _uow.Users.AnyAsync(u => u.Username == request.Username, ct))
            throw AppException.Conflict("Username already taken.");
        if (await _uow.Users.AnyAsync(u => u.Email == request.Email, ct))
            throw AppException.Conflict("Email already registered.");

        var role = Enum.Parse<UserRole>(request.Role, ignoreCase: true);
        var user = new AppUser
        {
            Username = request.Username,
            Email = request.Email,
            FullName = request.FullName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = role
        };

        await _uow.Users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<UserDto>.Ok(MapToDto(user), "User registered successfully.");
    }

    public async Task<ApiResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(userId, ct)
            ?? throw AppException.NotFound("User");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw AppException.BadRequest("Current password is incorrect.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Password changed successfully.");
    }

    public async Task<ApiResponse<UserDto>> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(userId, ct)
            ?? throw AppException.NotFound("User");
        return ApiResponse<UserDto>.Ok(MapToDto(user));
    }

    private static UserDto MapToDto(AppUser u) => new(
        u.Id, u.Username, u.Email, u.FullName,
        u.Role.ToString(), u.IsActive, u.LastLoginAt
    );
}
