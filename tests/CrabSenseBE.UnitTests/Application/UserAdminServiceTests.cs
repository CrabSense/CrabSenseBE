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
        _uow.Setup(u => u.Users).Returns(_users.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(_areas.Object);
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
            FarmingAreaId = role is UserRole.FarmManager or UserRole.FarmStaff ? AreaOne.Id : null,
            IsActive = isActive,
            RefreshToken = "refresh",
            RefreshTokenExpiry = DateTime.UtcNow.AddDays(10)
        };
        _store.Add(user);
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
                    OwnerId = Guid.NewGuid(),
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
            OwnerId = Guid.NewGuid(),
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
            Role = UserRole.FarmStaff,
            FarmingAreaId = otherArea.Id
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
}
