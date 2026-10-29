using System.Security.Claims;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Quản trị tài khoản: danh sách, tạo, bật/tắt, đặt lại mật khẩu.
/// SystemAdministrator quản lý mọi role; FarmAdministrator chỉ Manager/Staff.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = AppRoles.FarmManageWithPlatform)]
[Tags("00b. Users (Admin)")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserAdminService _users;
    private readonly IAuthService _auth;
    private readonly ICurrentUserService _currentUser;

    public UsersController(
        IUserAdminService users, IAuthService auth, ICurrentUserService currentUser)
    {
        _users = users;
        _auth = auth;
        _currentUser = currentUser;
    }


    /// <summary>
    /// [UPDATE] Update employee profile information.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), 200)]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        [FromBody] UpdateAdminUserRequest request,
        CancellationToken ct)
    {
        var result = await _users.UpdateUserAsync(
            id,
            request,
            CallerRole(),
            _currentUser.UserId,
            ct);

        return Ok(result);
    }

    /// <summary>
    /// [UPDATE] Change FarmManager/FarmStaff role.
    /// </summary>
    [HttpPatch("{id:guid}/role")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), 200)]
    public async Task<IActionResult> ChangeRole(
        Guid id,
        [FromBody] ChangeUserRoleRequest request,
        CancellationToken ct)
    {
        var result = await _users.ChangeRoleAsync(
            id,
            request,
            CallerRole(),
            _currentUser.UserId,
            ct);

        return Ok(result);
    }


    /// <summary>[READ] Danh sách tài khoản trong phạm vi quản lý.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _users.ListAsync(CallerRole(), _currentUser.UserId, ct));

    /// <summary>[READ] Chi tiết một tài khoản.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _users.GetByIdAsync(id, CallerRole(), _currentUser.UserId, ct));

    /// <summary>[CREATE] Tạo tài khoản — giữ ma trận quyền chung và gán khu qua AuthService.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] RegisterRequest request, CancellationToken ct)
        => Ok(await _auth.RegisterAsync(request, CallerRole(), _currentUser.UserId, ct));

    /// <summary>[UPDATE] Bật / tắt tài khoản. Tắt sẽ thu hồi refresh token.</summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(
        Guid id, [FromBody] SetUserActiveRequest request, CancellationToken ct)
        => Ok(await _users.SetActiveAsync(id, request.IsActive, CallerRole(), _currentUser.UserId, ct));

    /// <summary>[ACTION] Đặt lại mật khẩu (thu hồi refresh token).</summary>
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(
        Guid id, [FromBody] ResetUserPasswordRequest request, CancellationToken ct)
        => Ok(await _users.ResetPasswordAsync(id, request.NewPassword, CallerRole(), _currentUser.UserId, ct));

    private UserRole CallerRole()
    {
        var raw = User.FindFirstValue(ClaimTypes.Role);
        if (!UserRoleParsing.TryParse(raw, out var role))
            throw AppException.Forbidden("Không xác định được role của người gọi.");
        return role;
    }
}
