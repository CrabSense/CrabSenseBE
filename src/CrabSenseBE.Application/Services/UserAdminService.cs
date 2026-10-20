using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Quản trị tài khoản. Ma trận quyền giống <see cref="AuthService.RegisterAsync"/>:
/// SystemAdministrator quản lý mọi tài khoản; FarmAdministrator chỉ quản lý Manager/Staff.
/// </summary>
public class UserAdminService : IUserAdminService
{
    private const int MinPasswordLength = 8;

    private readonly IUnitOfWork _uow;

    public UserAdminService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<IEnumerable<UserDto>>> ListAsync(
        UserRole callerRole, CancellationToken ct = default)
    {
        var users = await _uow.Users.GetAllAsync(ct);
        var visible = callerRole == UserRole.SystemAdministrator
            ? users
            : users.Where(u => CanManage(callerRole, u.Role));

        return ApiResponse<IEnumerable<UserDto>>.Ok(
            visible.OrderBy(u => u.Username).Select(AuthService.MapToDto).ToList());
    }

    public async Task<ApiResponse<UserDto>> GetByIdAsync(
        Guid id, UserRole callerRole, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("User");
        if (callerRole != UserRole.SystemAdministrator && !CanManage(callerRole, user.Role))
            throw AppException.NotFound("User");

        return ApiResponse<UserDto>.Ok(AuthService.MapToDto(user));
    }

    public async Task<ApiResponse<UserDto>> SetActiveAsync(
        Guid id, bool isActive, UserRole callerRole, Guid callerId, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("User");

        if (!CanManage(callerRole, user.Role))
            throw AppException.Forbidden("Bạn không có quyền quản lý tài khoản này.");

        // Tự khoá mình = tự đá khỏi hệ thống, không có đường vào lại ngoài DB.
        if (user.Id == callerId && !isActive)
            throw AppException.BadRequest("Không thể tự vô hiệu hoá tài khoản đang đăng nhập.");

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;
        if (!isActive)
        {
            // Thu hồi phiên: token cũ hết hạn theo access token, refresh token chết ngay.
            user.RefreshToken = null;
            user.RefreshTokenExpiry = null;
        }

        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<UserDto>.Ok(
            AuthService.MapToDto(user),
            isActive ? "Đã kích hoạt tài khoản." : "Đã vô hiệu hoá tài khoản.");
    }

    public async Task<ApiResponse> ResetPasswordAsync(
        Guid id, string newPassword, UserRole callerRole, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < MinPasswordLength)
            throw AppException.BadRequest(
                $"Mật khẩu mới phải có ít nhất {MinPasswordLength} ký tự.");

        var user = await _uow.Users.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("User");

        if (!CanManage(callerRole, user.Role))
            throw AppException.Forbidden("Bạn không có quyền quản lý tài khoản này.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        user.UpdatedAt = DateTime.UtcNow;

        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse.Ok("Đã đặt lại mật khẩu.");
    }

    /// <summary>SystemAdministrator: mọi role. FarmAdministrator: chỉ Manager/Staff.</summary>
    private static bool CanManage(UserRole caller, UserRole target) => caller switch
    {
        UserRole.SystemAdministrator => true,
        UserRole.FarmAdministrator =>
            target is UserRole.FarmManager or UserRole.FarmStaff,
        _ => false
    };
}
