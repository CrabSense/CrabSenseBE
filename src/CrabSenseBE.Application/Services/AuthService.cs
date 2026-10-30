using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class AuthService : IAuthService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IUnitOfWork _uow;
    private readonly IJwtService _jwt;

    public AuthService(IUnitOfWork uow, IJwtService jwt)
    {
        _uow = uow;
        _jwt = jwt;
    }

    public async Task<ApiResponse<LoginResponse>> LoginAsync(
        LoginRequest request, CancellationToken ct = default, string? ipAddress = null, string? userAgent = null)
    {
        var user = await _uow.Users.FirstOrDefaultAsync(
            u => (u.Username == request.Username || u.Email == request.Username) && u.IsActive, ct);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            if (user is not null)
            {
                await _uow.OperationLogs.AddAsync(new OperationLog
                {
                    UserId = user.Id,
                    Action = "LoginFailed",
                    EntityType = "AppUser",
                    EntityId = user.Id,
                    Details = request.Username,
                    IpAddress = ipAddress,
                }, ct);
                await _uow.SaveChangesAsync(ct);
            }
            throw AppException.Unauthorized("Invalid username or password.");
        }

        var accessToken = _jwt.GenerateAccessToken(user);
        var refreshToken = _jwt.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(30);
        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = ipAddress;
        user.LastUserAgent = userAgent;
        _uow.Users.Update(user);
        await _uow.OperationLogs.AddAsync(new OperationLog
        {
            UserId = user.Id,
            Action = "Login",
            EntityType = "AppUser",
            EntityId = user.Id,
            IpAddress = ipAddress,
        }, ct);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<LoginResponse>.Ok(new LoginResponse(
            accessToken,
            refreshToken,
            DateTime.UtcNow.AddDays(30),
            MapToDto(user)
        ));
    }

    public async Task<ApiResponse<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)
            || request.RefreshToken.StartsWith("mock_", StringComparison.OrdinalIgnoreCase))
            throw AppException.Unauthorized("Invalid or expired refresh token.");

        var user = await _uow.Users.FirstOrDefaultAsync(
            u => u.RefreshToken == request.RefreshToken
                 && u.RefreshTokenExpiry > DateTime.UtcNow
                 && u.IsActive, ct);

        if (user is null)
            throw AppException.Unauthorized("Invalid or expired refresh token.");

        var accessToken = _jwt.GenerateAccessToken(user);
        var newRefresh = _jwt.GenerateRefreshToken();
        user.RefreshToken = newRefresh;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(30);
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<LoginResponse>.Ok(new LoginResponse(
            accessToken, newRefresh, DateTime.UtcNow.AddDays(30), MapToDto(user)));
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

    public async Task<ApiResponse<UserDto>> RegisterAsync(
        RegisterRequest request, UserRole callerRole, Guid callerId, CancellationToken ct = default)
    {
        if (request is null)
            throw AppException.BadRequest("Request is required.");
        if (await _uow.Users.AnyAsync(u => u.Username == request.Username, ct))
            throw AppException.Conflict("Username already taken.");
        if (await _uow.Users.AnyAsync(u => u.Email == request.Email, ct))
            throw AppException.Conflict("Email already registered.");

        if (!UserRoleParsing.TryParse(request.Role, out var role))
            throw AppException.BadRequest(
                "Role phải là FarmAdministrator | FarmManager | FarmStaff.");

        if (role == UserRole.SystemAdministrator)
            throw AppException.BadRequest(
                "Không thể đăng ký System Administrator qua API.");

        // Chỉ System Administrator được tạo Farm Administrator; Farm Administrator
        // chỉ được tạo Manager/Staff. Role khác không được tạo tài khoản.
        var allowed = callerRole switch
        {
            UserRole.SystemAdministrator => true,
            UserRole.FarmAdministrator => role != UserRole.FarmAdministrator,
            _ => false
        };
        if (!allowed)
            throw AppException.Forbidden(
                "Role của bạn không được phép tạo tài khoản với role này.");

        // Giữ nguyên ma trận quyền tạo tài khoản, đồng thời bắt buộc gán khu phù hợp với role.
        FarmingArea? area = null;
        List<FarmingArea> managedAreas = [];
        if (role == UserRole.FarmAdministrator)
        {
            var areaIds = request.FarmingAreaIds?.Distinct().ToList() ?? [];
            if (request.FarmingAreaId.HasValue || areaIds.Count == 0 || areaIds.Any(id => id == Guid.Empty))
                throw AppException.BadRequest(
                    "Phải chọn ít nhất một khu/trang trại cho FarmAdministrator.");

            managedAreas = (await _uow.FarmingAreas.FindAsync(item => areaIds.Contains(item.Id), ct)).ToList();
            if (managedAreas.Count != areaIds.Count)
                throw AppException.BadRequest("Một hoặc nhiều khu/trang trại không tồn tại.");
        }
        else
        {
            if (!request.FarmingAreaId.HasValue || request.FarmingAreaId == Guid.Empty)
                throw AppException.BadRequest("FarmingAreaId là bắt buộc cho FarmManager/FarmStaff.");
            if (request.FarmingAreaIds is { Count: > 0 })
                throw AppException.BadRequest("FarmManager/FarmStaff chỉ được gắn với một khu/trang trại.");

            area = await _uow.FarmingAreas.GetByIdAsync(request.FarmingAreaId.Value, ct)
                ?? throw AppException.NotFound("FarmingArea");
            // Farm Administrator chỉ được gán nhân viên vào khu mình đang quản lý.
            if (callerRole == UserRole.FarmAdministrator)
            {
                var managesArea = await _uow.FarmingAreas.AnyAsync(
                    item => item.Id == area.Id &&
                            item.Administrators.Any(admin => admin.Id == callerId),
                    ct);
                if (!managesArea)
                    throw AppException.Forbidden(
                        "Bạn chỉ được tạo tài khoản trong khu/trang trại mình quản lý.");
            }
        }

        //thêm EmployeeID
        var employeeNumber = await _uow.GetNextEmployeeNumberAsync(ct);
        var employeeId = $"EMP-{employeeNumber:D6}";

        while (await _uow.Users.AnyAsync(
                   user => user.EmployeeId == employeeId,
                   ct))
        {
            employeeNumber = await _uow.GetNextEmployeeNumberAsync(ct);
            employeeId = $"EMP-{employeeNumber:D6}";
        }

        var user = new AppUser
        {
            Username = request.Username,
            Email = request.Email,
            FullName = request.FullName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = role,
            EmployeeId = employeeId,
            FarmingAreaId = area?.Id,
            FarmingArea = area,
            ManagedAreas = managedAreas
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
        user.UpdatedAt = DateTime.UtcNow;
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

    public async Task<ApiResponse<UserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(userId, ct)
            ?? throw AppException.NotFound("User");

        if (!string.IsNullOrWhiteSpace(request.Email)
            && !string.Equals(request.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            if (await _uow.Users.AnyAsync(u => u.Email == request.Email && u.Id != userId, ct))
                throw AppException.Conflict("Email already registered.");
            user.Email = request.Email.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
            user.FullName = request.FullName.Trim();

        if (request.Phone is not null)
            user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        if (request.EmployeeId is not null)
            user.EmployeeId = string.IsNullOrWhiteSpace(request.EmployeeId) ? null : request.EmployeeId.Trim();

        if (request.AvatarUrl is not null)
            user.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();

        user.UpdatedAt = DateTime.UtcNow;
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<UserDto>.Ok(MapToDto(user), "Profile updated.");
    }

    public async Task<ApiResponse<NotificationPreferencesDto>> GetNotificationPreferencesAsync(
        Guid userId, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(userId, ct)
            ?? throw AppException.NotFound("User");
        return ApiResponse<NotificationPreferencesDto>.Ok(ParsePrefs(user.NotificationPrefsJson));
    }

    public async Task<ApiResponse<NotificationPreferencesDto>> UpdateNotificationPreferencesAsync(
        Guid userId, NotificationPreferencesDto request, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(userId, ct)
            ?? throw AppException.NotFound("User");

        var prefs = new NotificationPreferencesDto(
            request.WarningsEnabled,
            request.TaskRemindersEnabled,
            request.SystemUpdatesEnabled,
            request.SoundEnabled,
            request.VibrationEnabled,
            request.LedIndicatorEnabled);

        user.NotificationPrefsJson = JsonSerializer.Serialize(prefs, JsonOpts);
        user.UpdatedAt = DateTime.UtcNow;
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<NotificationPreferencesDto>.Ok(prefs, "Notification preferences updated.");
    }

    private static NotificationPreferencesDto ParsePrefs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new NotificationPreferencesDto();

        try
        {
            return JsonSerializer.Deserialize<NotificationPreferencesDto>(json, JsonOpts)
                   ?? new NotificationPreferencesDto();
        }
        catch
        {
            return new NotificationPreferencesDto();
        }
    }

    internal static UserDto MapToDto(AppUser u) => new(
        u.Id,
        u.Username,
        u.Email,
        u.FullName,
        u.Role.ToString(),
        u.IsActive,
        u.LastLoginAt,
        u.Phone,
        u.EmployeeId,
        u.AvatarUrl,
        u.CreatedAt,
        u.FarmingAreaId,
        // EF không Include thì collection có thể null — không được ném 500 lúc login.
        u.ManagedAreas?.Select(area => area.Id).ToList()
    );
}
