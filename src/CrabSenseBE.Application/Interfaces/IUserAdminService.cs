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
    /// <summary>Liệt kê tài khoản theo quyền và các khu mà người gọi được quản lý.</summary>
    Task<ApiResponse<IEnumerable<UserDto>>> ListAsync(
        UserRole callerRole, Guid callerId, CancellationToken ct = default);

    /// <summary>Lấy chi tiết tài khoản nếu người gọi có quyền quản lý.</summary>
    Task<ApiResponse<UserDto>> GetByIdAsync(
        Guid id, UserRole callerRole, Guid callerId, CancellationToken ct = default);

    Task<ApiResponse<UserDto>> SetActiveAsync(
        Guid id, bool isActive, UserRole callerRole, Guid callerId, CancellationToken ct = default);

    Task<ApiResponse> ResetPasswordAsync(
        Guid id, string newPassword, UserRole callerRole, Guid callerId, CancellationToken ct = default);


    /// <summary>Cập nhật hồ sơ nhân viên trong phạm vi quản lý của người gọi.</summary>
    Task<ApiResponse<UserDto>> UpdateUserAsync(
        Guid id,
        UpdateAdminUserRequest request,
        UserRole callerRole,
        Guid callerId,
        CancellationToken ct = default);

    /// <summary>Đổi role nhân viên theo ma trận quyền và phạm vi quản lý của người gọi.</summary>
    Task<ApiResponse<UserDto>> ChangeRoleAsync(
        Guid id,
        ChangeUserRoleRequest request,
        UserRole callerRole,
        Guid callerId,
        CancellationToken ct = default);

}
