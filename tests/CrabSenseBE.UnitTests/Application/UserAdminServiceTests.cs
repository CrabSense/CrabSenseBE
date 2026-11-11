using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>
/// Ma trận quản trị tài khoản: SystemAdministrator quản lý tất cả,
/// FarmAdministrator chỉ Manager/Staff trong khu được giao, chặn tự vô hiệu hoá.
/// </summary>
public class UserAdminServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IRepository<AppUser>> _users = new();
    private readonly Mock<IRepository<FarmingArea>> _areas = new();
    private readonly List<AppUser> _store = new();
    private readonly List<FarmingArea> _areaStore = new();
    private readonly List<FarmManagerAssignment> _managerAssignments = new();
    private readonly List<FarmStaffAssignment> _staffAssignments = new();

    public UserAdminServiceTests()
    {
        _users.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _store.ToList());
        _users.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _store.FirstOrDefault(u => u.Id == id));
        _users.Setup(r => r.Update(It.IsAny<AppUser>()));
        _areas.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FarmingArea, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<FarmingArea, bool>> predicate, CancellationToken _) =>
                _areaStore.AsQueryable().Where(predicate).ToList());
        var managers = new Mock<IRepository<FarmManagerAssignment>>();
        managers.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _managerAssignments.ToList());
        managers.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>> predicate, CancellationToken _) =>
                _managerAssignments.AsQueryable().FirstOrDefault(predicate));
        managers.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>> predicate,
                CancellationToken _) => _managerAssignments.Where(predicate.Compile()).ToList());
        managers.Setup(r => r.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>> predicate,
                CancellationToken _) => _managerAssignments.Any(predicate.Compile()));
        managers.Setup(r => r.AddAsync(
                It.IsAny<FarmManagerAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<FarmManagerAssignment, CancellationToken>((item, _) =>
                _managerAssignments.Add(item))
            .Returns(Task.CompletedTask);
        managers.Setup(r => r.Remove(It.IsAny<FarmManagerAssignment>()))
            .Callback<FarmManagerAssignment>(item => _managerAssignments.Remove(item));
        var staff = new Mock<IRepository<FarmStaffAssignment>>();
        staff.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _staffAssignments.ToList());
        staff.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>> predicate, CancellationToken _) =>
                _staffAssignments.AsQueryable().FirstOrDefault(predicate));
        staff.Setup(r => r.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>> predicate,
                CancellationToken _) => _staffAssignments.Any(predicate.Compile()));
        _uow.Setup(u => u.Users).Returns(_users.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(_areas.Object);
        _uow.Setup(u => u.FarmManagerAssignments).Returns(managers.Object);
        _uow.Setup(u => u.FarmStaffAssignments).Returns(staff.Object);
        _uow.Setup(u => u.CrabLots).Returns(EmptyRepo<CrabLot>().Object);
        _uow.Setup(u => u.CrabLotAreas).Returns(EmptyRepo<CrabLotArea>().Object);
        _uow.Setup(u => u.OperationLogs).Returns(EmptyRepo<OperationLog>().Object);
        _uow.Setup(u => u.GetFarmAdministratorAreaAssignmentsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> userIds, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>>)_areaStore
                    .SelectMany(area => area.Administrators
                        .Where(admin => userIds.Contains(admin.Id))
                        .Select(admin => new { UserId = admin.Id, FarmingAreaId = area.Id }))
                    .GroupBy(row => row.UserId)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyCollection<Guid>)group.Select(row => row.FarmingAreaId).ToList()));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private UserAdminService Create() => new(_uow.Object);

    private AppUser Add(UserRole role, bool isActive = true)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = $"{role}_{Guid.NewGuid():N}"[..18],
            Email = $"{Guid.NewGuid():N}@example.com",
            FullName = "Test",
            PasswordHash = "hash",
            Role = role,
            IsActive = isActive,
            RefreshToken = "refresh",
            RefreshTokenExpiry = DateTime.UtcNow.AddDays(10)
        };
        _store.Add(user);
        if (role == UserRole.FarmManager)
            _managerAssignments.Add(new FarmManagerAssignment
            {
                ManagerUserId = user.Id,
                FarmingAreaId = AreaOne.Id,
                AssignedByUserId = Guid.NewGuid()
            });
        if (role == UserRole.FarmStaff)
            _staffAssignments.Add(new FarmStaffAssignment
            {
                StaffUserId = user.Id,
                ManagerUserId = Guid.NewGuid(),
                FarmingAreaId = AreaOne.Id,
                AssignedByUserId = Guid.NewGuid()
            });
        return user;
    }

    private FarmingArea AreaOne
    {
        get
        {
            if (_areaStore.Count == 0)
                _areaStore.Add(new FarmingArea
                {
                    Id = Guid.NewGuid(),
                    Code = "AREA-1",
                    Name = "Area 1"
                });
            return _areaStore[0];
        }
    }

    private AppUser AddFarmAdministrator()
    {
        var administrator = Add(UserRole.FarmAdministrator);
        AreaOne.Administrators.Add(administrator);
        return administrator;
    }

    [Fact]
    public async Task SystemAdministrator_SeesEveryRole()
    {
        Add(UserRole.SystemAdministrator);
        var farmAdministrator = AddFarmAdministrator();
        Add(UserRole.FarmManager);
        Add(UserRole.FarmStaff);

        var result = await Create().ListAsync(UserRole.SystemAdministrator, Guid.NewGuid());

        var users = result.Data!;
        users.Should().HaveCount(4);
        users.Single(user => user.Id == farmAdministrator.Id)
            .FarmingAreaIds.Should().Contain(AreaOne.Id);
    }

    [Fact]
    public async Task FarmAdministrator_SeesOnlyManagerAndStaff()
    {
        var admin = AddFarmAdministrator();
        Add(UserRole.FarmManager);
        Add(UserRole.FarmStaff);
        var otherArea = new FarmingArea
        {
            Id = Guid.NewGuid(),
            Code = "AREA-2",
            Name = "Area 2"
        };
        _areaStore.Add(otherArea);
        _store.Add(new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "other_farm_staff",
            Email = "other@example.com",
            FullName = "Other",
            Role = UserRole.FarmStaff
        });
        _staffAssignments.Add(new FarmStaffAssignment
        {
            StaffUserId = _store[^1].Id,
            ManagerUserId = Guid.NewGuid(),
            FarmingAreaId = otherArea.Id,
            AssignedByUserId = Guid.NewGuid()
        });

        var result = await Create().ListAsync(UserRole.FarmAdministrator, admin.Id);

        result.Data!.Select(u => u.Role).Should().BeEquivalentTo(
            new[] { nameof(UserRole.FarmManager), nameof(UserRole.FarmStaff) });
    }

    [Fact]
    public async Task FarmAdministrator_CannotDisableSystemAdministrator()
    {
        var target = Add(UserRole.SystemAdministrator);

        var act = () => Create().SetActiveAsync(
            target.Id, false, UserRole.FarmAdministrator, Guid.NewGuid());

        await act.Should().ThrowAsync<AppException>();
        target.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task DisablingAUser_RevokesRefreshTokenSessions()
    {
        var admin = AddFarmAdministrator();
        var target = Add(UserRole.FarmStaff);

        var result = await Create().SetActiveAsync(
            target.Id, false, UserRole.FarmAdministrator, admin.Id);

        result.Success.Should().BeTrue();
        target.IsActive.Should().BeFalse();
        target.RefreshToken.Should().BeNull();
        target.RefreshTokenExpiry.Should().BeNull();
    }

    [Fact]
    public async Task CannotDisableSelf()
    {
        var me = Add(UserRole.FarmAdministrator);

        var act = () => Create().SetActiveAsync(
            me.Id, false, UserRole.FarmAdministrator, me.Id);

        await act.Should().ThrowAsync<AppException>();
        me.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPassword_RejectsShortPassword()
    {
        var target = Add(UserRole.FarmStaff);

        var act = () => Create().ResetPasswordAsync(
            target.Id, "1234", UserRole.FarmAdministrator, Guid.NewGuid());

        await act.Should().ThrowAsync<AppException>();
    }

    [Fact]
    public async Task ResetPassword_HashesAndRevokesSessions()
    {
        var admin = AddFarmAdministrator();
        var target = Add(UserRole.FarmStaff);

        var result = await Create().ResetPasswordAsync(
            target.Id, "NewSecret!123", UserRole.FarmAdministrator, admin.Id);

        result.Success.Should().BeTrue();
        target.PasswordHash.Should().NotBe("hash");
        BCrypt.Net.BCrypt.Verify("NewSecret!123", target.PasswordHash).Should().BeTrue();
        target.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task Administrator_updates_manager_to_two_areas_without_duplicates()
    {
        var admin = AddFarmAdministrator();
        var areaB = AddArea("AREA-2", admin);
        var manager = Add(UserRole.FarmManager);

        var result = await Create().UpdateManagerFarmingAreasAsync(
            manager.Id,
            new CrabSenseBE.Application.DTOs.Auth.UpdateManagerFarmingAreasRequest(
                [AreaOne.Id, areaB.Id, areaB.Id]),
            UserRole.FarmAdministrator,
            admin.Id);

        result.Data!.FarmingAreaId.Should().BeNull();
        result.Data.FarmingAreaIds.Should().BeEquivalentTo([AreaOne.Id, areaB.Id]);
        _managerAssignments
            .Where(item => item.ManagerUserId == manager.Id)
            .Select(item => item.FarmingAreaId)
            .Should().BeEquivalentTo([AreaOne.Id, areaB.Id]);
    }

    [Fact]
    public async Task Administrator_cannot_manage_whole_manager_account_with_foreign_area()
    {
        var adminA = AddFarmAdministrator();
        var adminB = Add(UserRole.FarmAdministrator);
        var areaB = AddArea("AREA-B", adminB);
        var manager = Add(UserRole.FarmManager);
        _managerAssignments.Add(new FarmManagerAssignment
        {
            ManagerUserId = manager.Id,
            FarmingAreaId = areaB.Id,
            AssignedByUserId = adminB.Id
        });

        var act = () => Create().SetActiveAsync(
            manager.Id, false, UserRole.FarmAdministrator, adminA.Id);

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(403);
        manager.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Administrator_can_add_own_area_without_changing_foreign_assignment()
    {
        var adminA = AddFarmAdministrator();
        var adminB = Add(UserRole.FarmAdministrator);
        var areaB = AddArea("AREA-B", adminB);
        var manager = Add(UserRole.FarmManager);
        _managerAssignments.RemoveAll(item => item.ManagerUserId == manager.Id);
        _managerAssignments.Add(new FarmManagerAssignment
        {
            ManagerUserId = manager.Id,
            FarmingAreaId = areaB.Id,
            AssignedByUserId = adminB.Id
        });

        var result = await Create().UpdateManagerFarmingAreasAsync(
            manager.Id,
            new CrabSenseBE.Application.DTOs.Auth.UpdateManagerFarmingAreasRequest(
                [areaB.Id, AreaOne.Id]),
            UserRole.FarmAdministrator,
            adminA.Id);

        result.Data!.FarmingAreaIds.Should().BeEquivalentTo(
            [areaB.Id, AreaOne.Id]);
        _managerAssignments.Should().Contain(item =>
            item.ManagerUserId == manager.Id
            && item.FarmingAreaId == areaB.Id
            && item.AssignedByUserId == adminB.Id);
    }

    [Fact]
    public async Task Cannot_remove_manager_area_while_staff_is_still_assigned()
    {
        var admin = AddFarmAdministrator();
        var areaB = AddArea("AREA-2", admin);
        var manager = Add(UserRole.FarmManager);
        _managerAssignments.Add(new FarmManagerAssignment
        {
            ManagerUserId = manager.Id,
            FarmingAreaId = areaB.Id,
            AssignedByUserId = admin.Id
        });
        _staffAssignments.Add(new FarmStaffAssignment
        {
            StaffUserId = Guid.NewGuid(),
            ManagerUserId = manager.Id,
            FarmingAreaId = areaB.Id,
            AssignedByUserId = admin.Id
        });

        var act = () => Create().UpdateManagerFarmingAreasAsync(
            manager.Id,
            new CrabSenseBE.Application.DTOs.Auth.UpdateManagerFarmingAreasRequest(
                [AreaOne.Id]),
            UserRole.FarmAdministrator,
            admin.Id);

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(409);
        _managerAssignments.Should().Contain(item =>
            item.ManagerUserId == manager.Id && item.FarmingAreaId == areaB.Id);
    }

    [Fact]
    public async Task Cannot_remove_area_when_active_multi_area_lot_loses_full_manager_coverage()
    {
        var admin = AddFarmAdministrator();
        var areaB = AddArea("AREA-2", admin);
        var manager = Add(UserRole.FarmManager);
        _managerAssignments.Add(new FarmManagerAssignment
        {
            ManagerUserId = manager.Id,
            FarmingAreaId = areaB.Id,
            AssignedByUserId = admin.Id
        });
        var lotId = Guid.NewGuid();
        var lots = new Mock<IRepository<CrabLot>>();
        lots.Setup(item => item.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CrabLot
                {
                    Id = lotId,
                    LotCode = "LOT-AB",
                    Status = "Allocating",
                    ClosedAt = null
                }
            });
        var links = new Mock<IRepository<CrabLotArea>>();
        links.Setup(item => item.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CrabLotArea { CrabLotId = lotId, FarmingAreaId = AreaOne.Id },
                new CrabLotArea { CrabLotId = lotId, FarmingAreaId = areaB.Id }
            });
        _uow.Setup(unit => unit.CrabLots).Returns(lots.Object);
        _uow.Setup(unit => unit.CrabLotAreas).Returns(links.Object);

        var act = () => Create().UpdateManagerFarmingAreasAsync(
            manager.Id,
            new CrabSenseBE.Application.DTOs.Auth.UpdateManagerFarmingAreasRequest(
                [AreaOne.Id]),
            UserRole.FarmAdministrator,
            admin.Id);

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Multi_area_manager_cannot_change_to_staff_and_leave_assignments()
    {
        var admin = AddFarmAdministrator();
        var areaB = AddArea("AREA-2", admin);
        var manager = Add(UserRole.FarmManager);
        _managerAssignments.Add(new FarmManagerAssignment
        {
            ManagerUserId = manager.Id,
            FarmingAreaId = areaB.Id,
            AssignedByUserId = admin.Id
        });

        var act = () => Create().ChangeRoleAsync(
            manager.Id,
            new CrabSenseBE.Application.DTOs.Auth.ChangeUserRoleRequest(
                nameof(UserRole.FarmStaff), Guid.NewGuid()),
            UserRole.FarmAdministrator,
            admin.Id);

        await act.Should().ThrowAsync<AppException>();
        manager.Role.Should().Be(UserRole.FarmManager);
        _managerAssignments.Count(item => item.ManagerUserId == manager.Id)
            .Should().Be(2);
    }

    [Fact]
    public async Task Legacy_single_area_manager_keeps_singular_compatibility_field()
    {
        var manager = Add(UserRole.FarmManager);

        var result = await Create().GetByIdAsync(
            manager.Id, UserRole.SystemAdministrator, Guid.NewGuid());

        result.Data!.FarmingAreaId.Should().Be(AreaOne.Id);
        result.Data.FarmingAreaIds.Should().Equal(AreaOne.Id);
    }

    private FarmingArea AddArea(string code, AppUser administrator)
    {
        var area = new FarmingArea
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code
        };
        area.Administrators.Add(administrator);
        _areaStore.Add(area);
        return area;
    }

    private static Mock<IRepository<T>> EmptyRepo<T>() where T : class
    {
        var repo = new Mock<IRepository<T>>();
        repo.Setup(item => item.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<T>());
        repo.Setup(item => item.AddAsync(
                It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repo;
    }
}
