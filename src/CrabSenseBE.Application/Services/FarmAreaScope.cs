using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Quyền theo khu lấy từ database: User → FarmingArea → FarmingRow → Box.
/// Không dùng FarmingAreaId do client gửi làm bằng chứng quyền.
/// </summary>
public sealed class FarmAreaScope
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService? _currentUser;

    public FarmAreaScope(IUnitOfWork uow, ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _currentUser = currentUser;
    }

    /// <summary>
    /// null chỉ dành cho unit test khi chủ động không truyền ICurrentUserService.
    /// Tập rỗng = người dùng đã xác thực nhưng không được phân công khu nào.
    /// </summary>
    public async Task<HashSet<Guid>?> AreaIdsAsync(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        if (user is null) return null;
        if (user.Role == UserRole.SystemAdministrator)
            return new HashSet<Guid>();
        return await AssignedAreaIdsAsync(user, ct);
    }

    /// <summary>Xem cấu hình kỹ thuật: System Administrator được xem; role trại chỉ xem khu được gán.</summary>
    public async Task EnsureReadAsync(Guid areaId, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        if (user is null || user.Role == UserRole.SystemAdministrator)
            return;
        await EnsureAsync(areaId, manage: false, ct);
    }

    /// <summary>
    /// null = unit test chủ động không truyền ICurrentUserService.
    /// Có khu trên query thì chỉ khu đó, sau khi kiểm tra phân công.
    /// Không truyền khu thì toàn bộ khu được phân công.
    /// </summary>
    public async Task<HashSet<Guid>?> SelectionAsync(Guid? requestedAreaId, CancellationToken ct)
    {
        if (requestedAreaId is Guid areaId && areaId != Guid.Empty)
        {
            await EnsureAsync(areaId, manage: false, ct);
            return new HashSet<Guid> { areaId };
        }

        return await AreaIdsAsync(ct);
    }

    public async Task EnsureAsync(Guid areaId, bool manage, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        if (user is null) return;
        if (user.Role == UserRole.SystemAdministrator)
            throw AppException.Forbidden("System Administrator không có quyền dữ liệu nghiệp vụ trang trại.");

        var allowed = await AssignedAreaIdsAsync(user, ct);
        if (!allowed.Contains(areaId))
            throw AppException.Forbidden("Bạn không được phân công khu này.");
        if (manage && user.Role != UserRole.FarmAdministrator)
            throw AppException.Forbidden("Chỉ Farm Administrator được phân công khu này mới được sửa cấu trúc.");
    }

    public async Task EnsureAllAssignedAsync(
        IEnumerable<Guid> areaIds,
        CancellationToken ct)
    {
        var required = areaIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        var allowed = await AreaIdsAsync(ct);
        if (allowed is null) return;
        if (required.Length == 0 || required.Any(id => !allowed.Contains(id)))
        {
            throw AppException.Forbidden(
                "Bạn phải được phân công tất cả khu của Crab Lot để quản lý toàn lô.");
        }
    }

    public async Task<AppUser?> CurrentUserAsync(CancellationToken ct)
    {
        if (_currentUser is null)
            return null;
        if (!_currentUser.IsAuthenticated)
            throw AppException.Unauthorized("Người dùng chưa được xác thực.");
        return await _uow.Users.GetByIdAsync(_currentUser.UserId, ct)
            ?? throw AppException.Unauthorized("Không xác định được người dùng.");
    }

    public async Task<Guid> AreaIdOfBoxAsync(Box box, CancellationToken ct)
    {
        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct)
            ?? throw AppException.NotFound("FarmingRow");
        return row.FarmingAreaId;
    }

    public async Task EnsureBoxAsync(Guid boxId, bool manage, CancellationToken ct)
    {
        if (_currentUser is null)
            return;
        _ = await CurrentUserAsync(ct);
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");
        await EnsureAsync(await AreaIdOfBoxAsync(box, ct), manage, ct);
    }

    public async Task EnsureRowAsync(Guid rowId, bool manage, CancellationToken ct)
    {
        if (_currentUser is null)
            return;
        _ = await CurrentUserAsync(ct);
        var row = await _uow.FarmingRows.GetByIdAsync(rowId, ct)
            ?? throw AppException.NotFound("FarmingRow");
        await EnsureAsync(row.FarmingAreaId, manage, ct);
    }

    private async Task<HashSet<Guid>> AssignedAreaIdsAsync(AppUser user, CancellationToken ct)
    {
        if (user.Role == UserRole.FarmAdministrator)
        {
            var source = _uow.FarmingAreas.Query();
            if (source is null) return new HashSet<Guid>();
            var query = source
                .Where(area => area.Administrators.Any(admin => admin.Id == user.Id))
                .Select(area => area.Id);
            var ids = query.Provider is Microsoft.EntityFrameworkCore.Query.IAsyncQueryProvider
                ? await query.ToListAsync(ct)
                : query.ToList();
            return ids.ToHashSet();
        }

        if (user.Role == UserRole.FarmManager)
        {
            var assignments = await _uow.FarmManagerAssignments.FindAsync(
                item => item.ManagerUserId == user.Id, ct);
            return assignments
                .Select(item => item.FarmingAreaId)
                .Where(id => id != Guid.Empty)
                .ToHashSet();
        }

        if (user.Role == UserRole.FarmStaff)
        {
            var assignment = await _uow.FarmStaffAssignments.FirstOrDefaultAsync(
                item => item.StaffUserId == user.Id, ct);
            return assignment is null ? new HashSet<Guid>() : new HashSet<Guid> { assignment.FarmingAreaId };
        }

        return new HashSet<Guid>();
    }
}

/// <summary>Giới hạn báo cáo theo khu được phân công, rồi mới tính số liệu.</summary>
public sealed class ReportAreaGate
{
    private readonly IUnitOfWork _uow;
    private readonly FarmAreaScope _areas;

    public ReportAreaGate(IUnitOfWork uow, ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public Task<HashSet<Guid>?> AreasAsync(Guid? requestedAreaId, CancellationToken ct)
        => _areas.SelectionAsync(requestedAreaId, ct);

    public async Task<HashSet<Guid>?> BoxIdsAsync(Guid? requestedAreaId, CancellationToken ct)
    {
        var areas = await AreasAsync(requestedAreaId, ct);
        if (areas is null) return null;
        var rowQuery = _uow.FarmingRows.Query();
        var rowIds = (rowQuery is null
                ? (await _uow.FarmingRows.GetAllAsync(ct)).Where(row => areas.Contains(row.FarmingAreaId)).Select(row => row.Id).ToList()
                : rowQuery.Provider is Microsoft.EntityFrameworkCore.Query.IAsyncQueryProvider
                    ? await rowQuery.Where(row => areas.Contains(row.FarmingAreaId)).Select(row => row.Id).ToListAsync(ct)
                    : rowQuery.Where(row => areas.Contains(row.FarmingAreaId)).Select(row => row.Id).ToList())
            .ToHashSet();
        var boxQuery = _uow.Boxes.Query();
        if (boxQuery is not null)
        {
            var ids = boxQuery.Where(box => rowIds.Contains(box.FarmingRowId)).Select(box => box.Id);
            return (ids.Provider is Microsoft.EntityFrameworkCore.Query.IAsyncQueryProvider
                ? await ids.ToListAsync(ct) : ids.ToList()).ToHashSet();
        }
        return (await _uow.Boxes.GetAllAsync(ct)).Where(box => rowIds.Contains(box.FarmingRowId))
            .Select(box => box.Id)
            .ToHashSet();
    }
}
