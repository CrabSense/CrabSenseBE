using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using System.ComponentModel.DataAnnotations;

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
        UserRole callerRole, Guid callerId, CancellationToken ct = default)
    {
        var users = await _uow.Users.GetAllAsync(ct);
        var managerAssignments = (await _uow.FarmManagerAssignments.GetAllAsync(ct)).ToList();
        var staffAssignments = (await _uow.FarmStaffAssignments.GetAllAsync(ct)).ToList();
        IEnumerable<AppUser> visible = users;
        if (callerRole != UserRole.SystemAdministrator)
        {
            var areaIds = await GetManagedAreaIdsAsync(callerId, ct);
            visible = users.Where(user =>
                CanManageRole(callerRole, user.Role) &&
                AreaIdOf(user, managerAssignments, staffAssignments) is Guid areaId &&
                areaIds.Contains(areaId));
        }

        var visibleUsers = visible.ToList();
        // Truy vấn user chung không nạp các khu của Farm Administrator; lấy tập trung
        // một lần để trả đủ dữ liệu và tránh phát sinh truy vấn N+1.
        var farmAdministratorIds = visibleUsers
            .Where(user => user.Role == UserRole.FarmAdministrator)
            .Select(user => user.Id)
            .ToArray();
        var areaAssignments = farmAdministratorIds.Length == 0
            ? new Dictionary<Guid, IReadOnlyCollection<Guid>>()
            : await _uow.GetFarmAdministratorAreaAssignmentsAsync(farmAdministratorIds, ct);

        var result = visibleUsers
            .OrderBy(user => user.Username)
            .Select(user =>
            {
                var dto = AuthService.MapToDto(user);
                if (user.Role == UserRole.FarmAdministrator)
                    dto = dto with
                    {
                        FarmingAreaIds = areaAssignments.TryGetValue(user.Id, out var managedAreaIds)
                            ? managedAreaIds
                            : Array.Empty<Guid>()
                    };
                else if (user.Role == UserRole.FarmManager)
                {
                    var assignment = managerAssignments.FirstOrDefault(item => item.ManagerUserId == user.Id);
                    if (assignment is not null)
                        dto = dto with { FarmingAreaId = assignment.FarmingAreaId };
                }
                else if (user.Role == UserRole.FarmStaff)
                {
                    var assignment = staffAssignments.FirstOrDefault(item => item.StaffUserId == user.Id);
                    if (assignment is not null)
                        dto = dto with
                        {
                            FarmingAreaId = assignment.FarmingAreaId,
                            ManagerUserId = assignment.ManagerUserId
                        };
                }
                return dto;
            })
            .ToList();

        return ApiResponse<IEnumerable<UserDto>>.Ok(result);
    }

    public async Task<ApiResponse<UserDto>> GetByIdAsync(
        Guid id, UserRole callerRole, Guid callerId, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("User");
        if (!await CanManageAsync(callerRole, user, callerId, ct))
            throw AppException.NotFound("User");

        return ApiResponse<UserDto>.Ok(await ToDtoAsync(user, ct));
    }

    public async Task<ApiResponse<UserDto>> SetActiveAsync(
        Guid id, bool isActive, UserRole callerRole, Guid callerId, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("User");

        if (!await CanManageAsync(callerRole, user, callerId, ct))
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
            await ToDtoAsync(user, ct),
            isActive ? "Đã kích hoạt tài khoản." : "Đã vô hiệu hoá tài khoản.");
    }

    public async Task<ApiResponse> ResetPasswordAsync(
        Guid id, string newPassword, UserRole callerRole, Guid callerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < MinPasswordLength)
            throw AppException.BadRequest(
                $"Mật khẩu mới phải có ít nhất {MinPasswordLength} ký tự.");

        var user = await _uow.Users.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("User");

        if (!await CanManageAsync(callerRole, user, callerId, ct))
            throw AppException.Forbidden("Bạn không có quyền quản lý tài khoản này.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        user.UpdatedAt = DateTime.UtcNow;

        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse.Ok("Đã đặt lại mật khẩu.");
    }


    public async Task<ApiResponse<UserDto>> UpdateUserAsync(
        Guid id,
        UpdateAdminUserRequest request,
        UserRole callerRole,
        Guid callerId,
        CancellationToken ct = default)
    {
        if (request is null)
            throw AppException.BadRequest(
                "Request is required.");

        var user = await _uow.Users.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("User");

        // Check permission to manage target account
        if (!await CanManageAsync(callerRole, user, callerId, ct))
            throw AppException.Forbidden(
                "You cannot manage this account.");

        if (string.IsNullOrWhiteSpace(request.FullName))
            throw AppException.BadRequest(
                "Full name is required.");

        if (string.IsNullOrWhiteSpace(request.Email))
            throw AppException.BadRequest(
                "Email is required.");

        var fullName = request.FullName.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (!new EmailAddressAttribute().IsValid(email))
            throw AppException.BadRequest(
                "Invalid email format.");

        // Check duplicate email
        var emailExists = await _uow.Users.AnyAsync(
            u => u.Id != id &&
                u.Email.ToLower() == email,
            ct);

        if (emailExists)
            throw AppException.Conflict(
                "Email already registered.");

        // Update employee information
        user.FullName = fullName;
        user.Email = email;

        user.Phone = string.IsNullOrWhiteSpace(request.Phone)
            ? null
            : request.Phone.Trim();

        user.UpdatedAt = DateTime.UtcNow;

        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<UserDto>.Ok(
            await ToDtoAsync(user, ct),
            "User updated successfully.");
    }


    public async Task<ApiResponse<UserDto>> ChangeRoleAsync(
        Guid id,
        ChangeUserRoleRequest request,
        UserRole callerRole,
        Guid callerId,
        CancellationToken ct = default)
    {
        if (request is null)
            throw AppException.BadRequest(
                "Request is required.");

        var user = await _uow.Users.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("User");

        // Verify permission for current target role
        if (!await CanManageAsync(callerRole, user, callerId, ct))
            throw AppException.Forbidden(
                "You cannot manage this account.");

        if (string.IsNullOrWhiteSpace(request.Role))
            throw AppException.BadRequest(
                "Role is required.");

        if (!UserRoleParsing.TryParse(
                request.Role, out var newRole))
            throw AppException.BadRequest(
                "Invalid role.");

        // Only FarmManager and FarmStaff are allowed
        // when FarmAdministrator changes employee roles.
        if (callerRole == UserRole.FarmAdministrator &&
            newRole is not (
                UserRole.FarmManager or UserRole.FarmStaff))
        {
            throw AppException.Forbidden(
                "FarmAdministrator can only assign " +
                "FarmManager or FarmStaff.");
        }

        // Prevent assigning SystemAdministrator
        // through this endpoint.
        if (newRole == UserRole.SystemAdministrator)
            throw AppException.Forbidden(
                "Cannot assign SystemAdministrator role.");

        if (user.Role == newRole)
            return ApiResponse<UserDto>.Ok(
                await ToDtoAsync(user, ct),
                "User already has this role.");

        await MoveAssignmentForRoleChangeAsync(user, newRole, request.ManagerUserId, callerId, ct);

        user.Role = newRole;
        user.UserCode = await NextUserCodeAsync(newRole, ct);
        user.UpdatedAt = DateTime.UtcNow;

        // Invalidate the refresh token after role change.
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;

        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<UserDto>.Ok(
            await ToDtoAsync(user, ct),
            "User role updated successfully.");
    }



    /// <summary>SystemAdministrator: mọi role. FarmAdministrator: chỉ Manager/Staff.</summary>
    private async Task<bool> CanManageAsync(
        UserRole callerRole,
        AppUser target,
        Guid callerId,
        CancellationToken ct)
    {
        if (callerRole == UserRole.SystemAdministrator)
            return true;
        var areaId = await AssignedAreaIdAsync(target, ct);
        if (!CanManageRole(callerRole, target.Role) || areaId is null)
            return false;

        return (await _uow.FarmingAreas.FindAsync(
            area => area.Id == areaId.Value &&
                    area.Administrators.Any(admin => admin.Id == callerId),
            ct)).Any();
    }

    private async Task MoveAssignmentForRoleChangeAsync(
        AppUser user,
        UserRole newRole,
        Guid? managerUserId,
        Guid callerId,
        CancellationToken ct)
    {
        var currentAreaId = await AssignedAreaIdAsync(user, ct)
            ?? throw AppException.BadRequest("Tài khoản chưa được gán khu.");

        if (user.Role == UserRole.FarmManager && newRole == UserRole.FarmStaff)
        {
            if (await _uow.FarmStaffAssignments.AnyAsync(item => item.ManagerUserId == user.Id, ct))
                throw AppException.BadRequest("Hãy chuyển nhân viên sang manager khác trước khi đổi role manager này.");
            if (!managerUserId.HasValue || managerUserId == Guid.Empty || managerUserId == user.Id)
                throw AppException.BadRequest("FarmStaff phải được gán dưới một FarmManager của cùng khu.");
            var managerAssignment = await _uow.FarmManagerAssignments.FirstOrDefaultAsync(
                item => item.ManagerUserId == managerUserId.Value, ct)
                ?? throw AppException.BadRequest("FarmManager chưa được gán khu.");
            if (managerAssignment.FarmingAreaId != currentAreaId)
                throw AppException.BadRequest("FarmManager không quản lý khu của tài khoản này.");

            var own = await _uow.FarmManagerAssignments.FirstOrDefaultAsync(
                item => item.ManagerUserId == user.Id, ct);
            if (own is not null)
                _uow.FarmManagerAssignments.Remove(own);
            await _uow.FarmStaffAssignments.AddAsync(new FarmStaffAssignment
            {
                StaffUserId = user.Id,
                ManagerUserId = managerUserId.Value,
                FarmingAreaId = currentAreaId,
                AssignedByUserId = callerId
            }, ct);
            return;
        }

        if (user.Role == UserRole.FarmStaff && newRole == UserRole.FarmManager)
        {
            var own = await _uow.FarmStaffAssignments.FirstOrDefaultAsync(
                item => item.StaffUserId == user.Id, ct);
            if (own is not null)
                _uow.FarmStaffAssignments.Remove(own);
            await _uow.FarmManagerAssignments.AddAsync(new FarmManagerAssignment
            {
                ManagerUserId = user.Id,
                FarmingAreaId = currentAreaId,
                AssignedByUserId = callerId
            }, ct);
        }
    }

    private async Task<Guid?> AssignedAreaIdAsync(AppUser user, CancellationToken ct)
    {
        if (user.Role == UserRole.FarmManager)
            return (await _uow.FarmManagerAssignments.FirstOrDefaultAsync(
                item => item.ManagerUserId == user.Id, ct))?.FarmingAreaId;
        if (user.Role == UserRole.FarmStaff)
            return (await _uow.FarmStaffAssignments.FirstOrDefaultAsync(
                item => item.StaffUserId == user.Id, ct))?.FarmingAreaId;
        return null;
    }

    private static Guid? AreaIdOf(
        AppUser user,
        IReadOnlyCollection<FarmManagerAssignment> managers,
        IReadOnlyCollection<FarmStaffAssignment> staff)
    {
        if (user.Role == UserRole.FarmManager)
            return managers.FirstOrDefault(item => item.ManagerUserId == user.Id)?.FarmingAreaId;
        if (user.Role == UserRole.FarmStaff)
            return staff.FirstOrDefault(item => item.StaffUserId == user.Id)?.FarmingAreaId;
        return null;
    }

    private async Task<UserDto> ToDtoAsync(AppUser user, CancellationToken ct)
    {
        var dto = AuthService.MapToDto(user);
        if (user.Role == UserRole.FarmManager)
        {
            var assignment = await _uow.FarmManagerAssignments.FirstOrDefaultAsync(
                item => item.ManagerUserId == user.Id, ct);
            if (assignment is not null)
                dto = dto with { FarmingAreaId = assignment.FarmingAreaId };
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

    private async Task<HashSet<Guid>> GetManagedAreaIdsAsync(Guid callerId, CancellationToken ct) =>
        (await _uow.FarmingAreas.FindAsync(
            area => area.Administrators.Any(admin => admin.Id == callerId),
            ct)).Select(area => area.Id).ToHashSet();

    private static bool CanManageRole(UserRole caller, UserRole target) =>
        caller == UserRole.FarmAdministrator &&
        target is UserRole.FarmManager or UserRole.FarmStaff;
}
