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
            await ToDtoAsync(user, ct)
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
            accessToken, newRefresh, DateTime.UtcNow.AddDays(30), await ToDtoAsync(user, ct)));
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
        List<FarmingArea> managerAreas = [];
        if (role == UserRole.FarmAdministrator)
        {
            var areaIds = request.FarmingAreaIds?.Distinct().ToList() ?? [];
            if (request.FarmingAreaId.HasValue || areaIds.Count == 0 || areaIds.Any(id => id == Guid.Empty))
                throw AppException.BadRequest(
                    "Phải chọn ít nhất một khu/trang trại cho FarmAdministrator.");

            managedAreas = (await _uow.FarmingAreas.FindAsync(item => areaIds.Contains(item.Id), ct)).ToList();
            if (managedAreas.Count != areaIds.Count)
                throw AppException.BadRequest("Một hoặc nhiều khu/trang trại không tồn tại.");
            var takenAreaIds = (await _uow.FarmingAreas.FindAsync(
                item => areaIds.Contains(item.Id) && item.Administrators.Any(), ct))
                .Select(item => item.Id)
                .ToList();
            if (takenAreaIds.Count > 0)
                throw AppException.Conflict("Mỗi khu chỉ được gán một Farm Administrator.");
        }
        else if (role == UserRole.FarmManager)
        {
            var areaIds = (request.FarmingAreaIds ?? Array.Empty<Guid>())
                .Concat(request.FarmingAreaId is Guid legacyAreaId
                    ? new[] { legacyAreaId }
                    : Array.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (areaIds.Count == 0)
                throw AppException.BadRequest(
                    "Phải chọn ít nhất một khu/trang trại cho FarmManager.");
            if (request.ManagerUserId.HasValue)
                throw AppException.BadRequest("FarmManager không được gán ManagerUserId.");

            managerAreas = (await _uow.FarmingAreas.FindAsync(
                item => areaIds.Contains(item.Id), ct)).ToList();
            if (managerAreas.Count != areaIds.Count)
                throw AppException.BadRequest("Một hoặc nhiều khu/trang trại không tồn tại.");
            if (callerRole == UserRole.FarmAdministrator)
            {
                var allowedAreaIds = (await _uow.FarmingAreas.FindAsync(
                        item => areaIds.Contains(item.Id)
                            && item.Administrators.Any(admin => admin.Id == callerId),
                        ct))
                    .Select(item => item.Id)
                    .ToHashSet();
                if (areaIds.Any(id => !allowedAreaIds.Contains(id)))
                    throw AppException.Forbidden(
                        "Bạn chỉ được gán FarmManager vào các khu mình quản lý.");
            }
        }
        else
        {
            if (!request.FarmingAreaId.HasValue || request.FarmingAreaId == Guid.Empty)
                throw AppException.BadRequest("FarmingAreaId là bắt buộc cho FarmStaff.");
            if (request.FarmingAreaIds is { Count: > 0 })
                throw AppException.BadRequest("FarmStaff chỉ được gắn với một khu/trang trại.");

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

            if (role == UserRole.FarmStaff)
            {
                if (!request.ManagerUserId.HasValue || request.ManagerUserId == Guid.Empty)
                    throw AppException.BadRequest("FarmStaff phải được gán dưới một FarmManager của khu đó.");
                var managerHasArea = await _uow.FarmManagerAssignments.AnyAsync(
                    item => item.ManagerUserId == request.ManagerUserId.Value
                        && item.FarmingAreaId == area.Id, ct);
                if (!managerHasArea)
                    throw AppException.BadRequest("FarmManager không quản lý khu được chọn.");
            }
        }

        var userCode = await NextUserCodeAsync(role, ct);

        var user = new AppUser
        {
            Username = request.Username,
            Email = request.Email,
            FullName = request.FullName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = role,
            UserCode = userCode,
            ManagedAreas = managedAreas
        };

        await _uow.Users.AddAsync(user, ct);
        if (role == UserRole.FarmManager)
        {
            foreach (var managerArea in managerAreas)
            {
                await _uow.FarmManagerAssignments.AddAsync(new FarmManagerAssignment
                {
                    ManagerUserId = user.Id,
                    Manager = user,
                    FarmingAreaId = managerArea.Id,
                    FarmingArea = managerArea,
                    AssignedByUserId = callerId
                }, ct);
            }
        }
        else if (role == UserRole.FarmStaff && area is not null && request.ManagerUserId.HasValue)
        {
            await _uow.FarmStaffAssignments.AddAsync(new FarmStaffAssignment
            {
                StaffUserId = user.Id,
                Staff = user,
                ManagerUserId = request.ManagerUserId.Value,
                FarmingAreaId = area.Id,
                FarmingArea = area,
                AssignedByUserId = callerId
            }, ct);
        }

        if (role == UserRole.FarmManager)
            await ConfigAudit.WriteAsync(
                _uow,
                callerId,
                "AssignManagerFarmingAreas",
                "AppUser",
                user.Id,
                JsonSerializer.Serialize(new
                {
                    FarmingAreaIds = managerAreas.Select(item => item.Id),
                    AssignedAt = DateTime.UtcNow
                }),
                ct);
        await _uow.SaveChangesAsync(ct);
        var dto = await ToDtoAsync(user, ct);
        return ApiResponse<UserDto>.Ok(dto, "User registered successfully.");
    }

    private async Task<string> NextUserCodeAsync(UserRole role, CancellationToken ct)
    {
        var number = await _uow.GetNextUserCodeNumberAsync(role, ct);
        var userCode = UserCodes.Format(role, number);
        while (await _uow.Users.AnyAsync(user => user.UserCode == userCode, ct))
        {
            number = await _uow.GetNextUserCodeNumberAsync(role, ct);
            userCode = UserCodes.Format(role, number);
        }

        return userCode;
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
        return ApiResponse<UserDto>.Ok(await ToDtoAsync(user, ct));
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

        if (request.AvatarUrl is not null)
            user.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();

        user.UpdatedAt = DateTime.UtcNow;
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<UserDto>.Ok(await ToDtoAsync(user, ct), "Profile updated.");
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
        u.UserCode,
        u.AvatarUrl,
        u.CreatedAt,
        null,
        // EF không Include thì collection có thể null — không được ném 500 lúc login.
        u.ManagedAreas?.Select(area => area.Id).ToList()
    );

    private async Task<UserDto> ToDtoAsync(AppUser user, CancellationToken ct)
    {
        var dto = MapToDto(user);
        if (user.Role == UserRole.FarmManager)
        {
            var areaIds = (await _uow.FarmManagerAssignments.FindAsync(
                    item => item.ManagerUserId == user.Id, ct))
                .Select(item => item.FarmingAreaId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
            dto = dto with
            {
                FarmingAreaId = areaIds.Length == 1 ? areaIds[0] : null,
                FarmingAreaIds = areaIds
            };
        }
        else if (user.Role == UserRole.FarmStaff)
        {
            var assignment = await _uow.FarmStaffAssignments.FirstOrDefaultAsync(
                item => item.StaffUserId == user.Id, ct);
            if (assignment is not null)
                dto = dto with
                {
                    FarmingAreaId = assignment.FarmingAreaId,
                    ManagerUserId = assignment.ManagerUserId
                };
        }

        return dto;
    }
}
