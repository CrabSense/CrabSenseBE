using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>
/// Quản lý tài khoản: xem danh sách, bật/tắt, đặt lại mật khẩu.
/// Tạo tài khoản dùng chung <see cref="IAuthService.RegisterAsync"/> để giữ một ma trận quyền duy nhất.
/// </summary>
public interface IUserAdminService
{
    Task<ApiResponse<IEnumerable<UserDto>>> ListAsync(
        UserRole callerRole, CancellationToken ct = default);

    Task<ApiResponse<UserDto>> GetByIdAsync(
        Guid id, UserRole callerRole, CancellationToken ct = default);

    Task<ApiResponse<UserDto>> SetActiveAsync(
        Guid id, bool isActive, UserRole callerRole, Guid callerId, CancellationToken ct = default);

    Task<ApiResponse> ResetPasswordAsync(
        Guid id, string newPassword, UserRole callerRole, CancellationToken ct = default);


    /// <summary>
    /// Update employee profile information.
    /// </summary>
    Task<ApiResponse<UserDto>> UpdateUserAsync(
        Guid id,
        UpdateAdminUserRequest request,
        UserRole callerRole,
        CancellationToken ct = default);

    /// <summary>
    /// Change employee role.
    /// </summary>
    Task<ApiResponse<UserDto>> ChangeRoleAsync(
        Guid id,
        ChangeUserRoleRequest request,
        UserRole callerRole,
        CancellationToken ct = default);

}
