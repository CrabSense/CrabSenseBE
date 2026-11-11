using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

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
            {
                if (!CanManageRole(callerRole, user.Role))
                    return false;
                var assignedAreaIds = AreaIdsOf(
                    user, managerAssignments, staffAssignments);
                return assignedAreaIds.Count > 0
                    && assignedAreaIds.All(areaIds.Contains);
            });
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
                    var managerAreaIds = managerAssignments
                        .Where(item => item.ManagerUserId == user.Id)
                        .Select(item => item.FarmingAreaId)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToArray();
                    dto = dto with
                    {
                        FarmingAreaId = managerAreaIds.Length == 1
                            ? managerAreaIds[0]
                            : null,
                        FarmingAreaIds = managerAreaIds
                    };
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
        await ConfigAudit.WriteAsync(
            _uow, callerId, isActive ? "ActivateUser" : "DeactivateUser",
            "AppUser", user.Id, $"IsActive={isActive}", ct);
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
        await ConfigAudit.WriteAsync(
            _uow, callerId, "ResetUserPassword",
            "AppUser", user.Id, "Refresh token revoked", ct);
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
        await ConfigAudit.WriteAsync(
            _uow, callerId, "UpdateUser",
            "AppUser", user.Id,
            JsonSerializer.Serialize(new { user.FullName, user.Email, user.Phone }),
            ct);
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

        var previousRole = user.Role;
        await MoveAssignmentForRoleChangeAsync(user, newRole, request.ManagerUserId, callerId, ct);

        user.Role = newRole;
        user.UserCode = await NextUserCodeAsync(newRole, ct);
        user.UpdatedAt = DateTime.UtcNow;

        // Invalidate the refresh token after role change.
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;

        _uow.Users.Update(user);
        await ConfigAudit.WriteAsync(
            _uow, callerId, "ChangeUserRole",
            "AppUser", user.Id,
            JsonSerializer.Serialize(new { Before = previousRole, After = newRole }),
            ct);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<UserDto>.Ok(
            await ToDtoAsync(user, ct),
            "User role updated successfully.");
    }

    public async Task<ApiResponse<UserDto>> UpdateManagerFarmingAreasAsync(
        Guid id,
        UpdateManagerFarmingAreasRequest request,
        UserRole callerRole,
        Guid callerId,
        CancellationToken ct = default)
    {
        if (callerRole != UserRole.FarmAdministrator)
            throw AppException.Forbidden(
                "Chỉ Farm Administrator được thay đổi phân công khu của Manager.");
        if (request is null)
            throw AppException.BadRequest("Request is required.");

        var manager = await _uow.Users.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("User");
        if (manager.Role != UserRole.FarmManager)
            throw AppException.BadRequest("Tài khoản không phải FarmManager.");

        var requestedAreaIds = (request.FarmingAreaIds ?? Array.Empty<Guid>())
            .Where(areaId => areaId != Guid.Empty)
            .Distinct()
            .ToHashSet();
        if (requestedAreaIds.Count == 0)
            throw AppException.BadRequest("FarmManager phải được phân công ít nhất một khu.");

        var existing = (await _uow.FarmManagerAssignments.FindAsync(
                item => item.ManagerUserId == manager.Id, ct))
            .ToList();
        var existingAreaIds = existing
            .Select(item => item.FarmingAreaId)
            .ToHashSet();
        var managedAreaIds = await GetManagedAreaIdsAsync(callerId, ct);
        var foreignExistingAreaIds = existingAreaIds
            .Where(areaId => !managedAreaIds.Contains(areaId))
            .ToHashSet();
        if (!foreignExistingAreaIds.IsSubsetOf(requestedAreaIds))
            throw AppException.Forbidden(
                "Không được thu hồi phân công thuộc Farm Administrator khác.");

        var addedAreaIds = requestedAreaIds.Except(existingAreaIds).ToHashSet();
        var removedAreaIds = existingAreaIds.Except(requestedAreaIds).ToHashSet();
        if (addedAreaIds.Concat(removedAreaIds)
            .Any(areaId => !managedAreaIds.Contains(areaId)))
            throw AppException.Forbidden(
                "Bạn chỉ được thay đổi các khu mình đang quản lý.");

        var existingAreas = await _uow.FarmingAreas.FindAsync(
            area => requestedAreaIds.Contains(area.Id), ct);
        if (existingAreas.Select(area => area.Id).Distinct().Count()
            != requestedAreaIds.Count)
            throw AppException.BadRequest("Một hoặc nhiều khu/trang trại không tồn tại.");

        if (removedAreaIds.Count > 0
            && await _uow.FarmStaffAssignments.AnyAsync(
                item => item.ManagerUserId == manager.Id
                    && removedAreaIds.Contains(item.FarmingAreaId), ct))
            throw AppException.Conflict(
                "Hãy chuyển Farm Staff trong khu bị thu hồi sang Manager khác trước.");

        await EnsureActiveLotsRemainManageableAsync(
            manager.Id, requestedAreaIds, removedAreaIds, ct);

        foreach (var assignment in existing
                     .Where(item => removedAreaIds.Contains(item.FarmingAreaId)))
            _uow.FarmManagerAssignments.Remove(assignment);
        foreach (var areaId in addedAreaIds)
        {
            if (await _uow.FarmManagerAssignments.AnyAsync(
                    item => item.ManagerUserId == manager.Id
                        && item.FarmingAreaId == areaId, ct))
                throw AppException.Conflict(
                    "FarmManager đã được phân công khu này.");
            await _uow.FarmManagerAssignments.AddAsync(
                new FarmManagerAssignment
                {
                    ManagerUserId = manager.Id,
                    FarmingAreaId = areaId,
                    AssignedByUserId = callerId
                },
                ct);
        }

        await ConfigAudit.WriteAsync(
            _uow,
            callerId,
            "UpdateManagerFarmingAreas",
            "AppUser",
            manager.Id,
            JsonSerializer.Serialize(new
            {
                Before = existingAreaIds.OrderBy(areaId => areaId),
                After = requestedAreaIds.OrderBy(areaId => areaId),
                Added = addedAreaIds.OrderBy(areaId => areaId)
                    .Select(areaId => new { FarmingAreaId = areaId, AssignedByUserId = callerId }),
                Removed = existing
                    .Where(item => removedAreaIds.Contains(item.FarmingAreaId))
                    .Select(item => new
                    {
                        item.FarmingAreaId,
                        PreviousAssignedByUserId = item.AssignedByUserId,
                        RevokedByUserId = callerId
                    }),
                ChangedAt = DateTime.UtcNow
            }),
            ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<UserDto>.Ok(
            await ToDtoAsync(manager, ct),
            "Đã cập nhật các khu được phân công cho FarmManager.");
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
        if (!CanManageRole(callerRole, target.Role))
            return false;

        var targetAreaIds = await AssignedAreaIdsAsync(target, ct);
        if (targetAreaIds.Count == 0)
            return false;
        var managedAreaIds = await GetManagedAreaIdsAsync(callerId, ct);
        return targetAreaIds.All(managedAreaIds.Contains);
    }

    private async Task MoveAssignmentForRoleChangeAsync(
        AppUser user,
        UserRole newRole,
        Guid? managerUserId,
        Guid callerId,
        CancellationToken ct)
    {
        if (user.Role == UserRole.FarmManager && newRole == UserRole.FarmStaff)
        {
            var ownAssignments = (await _uow.FarmManagerAssignments.FindAsync(
                    item => item.ManagerUserId == user.Id, ct))
                .ToList();
            if (ownAssignments.Count != 1)
                throw AppException.BadRequest(
                    "FarmManager phải chỉ còn đúng một khu trước khi đổi sang FarmStaff.");
            var currentAreaId = ownAssignments[0].FarmingAreaId;
            if (await _uow.FarmStaffAssignments.AnyAsync(item => item.ManagerUserId == user.Id, ct))
                throw AppException.BadRequest("Hãy chuyển nhân viên sang manager khác trước khi đổi role manager này.");
            if (!managerUserId.HasValue || managerUserId == Guid.Empty || managerUserId == user.Id)
                throw AppException.BadRequest("FarmStaff phải được gán dưới một FarmManager của cùng khu.");
            var replacementManagesArea = await _uow.FarmManagerAssignments.AnyAsync(
                item => item.ManagerUserId == managerUserId.Value
                    && item.FarmingAreaId == currentAreaId, ct);
            if (!replacementManagesArea)
                throw AppException.BadRequest("FarmManager không quản lý khu của tài khoản này.");
            await EnsureActiveLotsRemainManageableAsync(
                user.Id,
                new HashSet<Guid>(),
                new HashSet<Guid> { currentAreaId },
                ct);

            foreach (var own in ownAssignments)
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
            var currentAreaId = (await _uow.FarmStaffAssignments.FirstOrDefaultAsync(
                    item => item.StaffUserId == user.Id, ct))?.FarmingAreaId
                ?? throw AppException.BadRequest("Tài khoản chưa được gán khu.");
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

    private async Task<HashSet<Guid>> AssignedAreaIdsAsync(
        AppUser user,
        CancellationToken ct)
    {
        if (user.Role == UserRole.FarmManager)
            return (await _uow.FarmManagerAssignments.FindAsync(
                    item => item.ManagerUserId == user.Id, ct))
                .Select(item => item.FarmingAreaId)
                .ToHashSet();
        if (user.Role == UserRole.FarmStaff)
        {
            var assignment = await _uow.FarmStaffAssignments.FirstOrDefaultAsync(
                item => item.StaffUserId == user.Id, ct);
            return assignment is null
                ? new HashSet<Guid>()
                : new HashSet<Guid> { assignment.FarmingAreaId };
        }
        return new HashSet<Guid>();
    }

    private static HashSet<Guid> AreaIdsOf(
        AppUser user,
        IReadOnlyCollection<FarmManagerAssignment> managers,
        IReadOnlyCollection<FarmStaffAssignment> staff)
    {
        if (user.Role == UserRole.FarmManager)
            return managers
                .Where(item => item.ManagerUserId == user.Id)
                .Select(item => item.FarmingAreaId)
                .ToHashSet();
        if (user.Role == UserRole.FarmStaff)
        {
            var assignment = staff.FirstOrDefault(item => item.StaffUserId == user.Id);
            return assignment is null
                ? new HashSet<Guid>()
                : new HashSet<Guid> { assignment.FarmingAreaId };
        }
        return new HashSet<Guid>();
    }

    private async Task<UserDto> ToDtoAsync(AppUser user, CancellationToken ct)
    {
        var dto = AuthService.MapToDto(user);
        if (user.Role == UserRole.FarmManager)
        {
            var areaIds = (await _uow.FarmManagerAssignments.FindAsync(
                    item => item.ManagerUserId == user.Id, ct))
                .Select(item => item.FarmingAreaId)
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

    private async Task EnsureActiveLotsRemainManageableAsync(
        Guid managerId,
        HashSet<Guid> replacementAreaIds,
        HashSet<Guid> removedAreaIds,
        CancellationToken ct)
    {
        if (removedAreaIds.Count == 0)
            return;

        var lotLinks = (await _uow.CrabLotAreas.GetAllAsync(ct))
            .GroupBy(link => link.CrabLotId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(link => link.FarmingAreaId)
                    .Where(areaId => areaId != Guid.Empty)
                    .Distinct()
                    .ToHashSet());
        var impactedLots = (await _uow.CrabLots.GetAllAsync(ct))
            .Where(lot => lot.ClosedAt is null
                && !string.Equals(
                    lot.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                && lotLinks.TryGetValue(lot.Id, out var areas)
                && areas.Overlaps(removedAreaIds))
            .ToList();
        if (impactedLots.Count == 0)
            return;

        var activeManagerIds = (await _uow.Users.GetAllAsync(ct))
            .Where(user => user.Role == UserRole.FarmManager && user.IsActive)
            .Select(user => user.Id)
            .ToHashSet();
        var assignmentsByManager = (await _uow.FarmManagerAssignments.GetAllAsync(ct))
            .Where(item => activeManagerIds.Contains(item.ManagerUserId))
            .GroupBy(item => item.ManagerUserId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.FarmingAreaId).ToHashSet());
        if (activeManagerIds.Contains(managerId))
            assignmentsByManager[managerId] = replacementAreaIds;

        foreach (var lot in impactedLots)
        {
            var requiredAreaIds = lotLinks[lot.Id];
            if (!assignmentsByManager.Values.Any(requiredAreaIds.IsSubsetOf))
                throw AppException.Conflict(
                    $"Không thể thu hồi khu vì Crab Lot '{lot.LotCode}' đang hoạt động "
                    + "sẽ không còn FarmManager có đủ quyền trên toàn bộ lô.");
        }
    }

    private async Task<HashSet<Guid>> GetManagedAreaIdsAsync(Guid callerId, CancellationToken ct) =>
        (await _uow.FarmingAreas.FindAsync(
            area => area.Administrators.Any(admin => admin.Id == callerId),
            ct)).Select(area => area.Id).ToHashSet();

    private static bool CanManageRole(UserRole caller, UserRole target) =>
        caller == UserRole.FarmAdministrator &&
        target is UserRole.FarmManager or UserRole.FarmStaff;
}
