using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>
/// Ma trận quyền tạo tài khoản: chỉ SystemAdministrator tạo được FarmAdministrator,
/// FarmAdministrator chỉ tạo được Manager/Staff, role khác bị chặn; kiểm tra thêm gán khu.
/// </summary>
public class AuthServiceRegisterTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IRepository<AppUser>> _users = new();
    private readonly Mock<IRepository<FarmingArea>> _areas = new();
    private readonly FarmingArea _area = new()
    {
        Id = Guid.NewGuid(),
        Code = "AREA-1",
        Name = "Area 1"
    };
    private readonly FarmingArea _freeArea = new()
    {
        Id = Guid.NewGuid(),
        Code = "AREA-2",
        Name = "Area 2"
    };
    private readonly FarmingArea _secondManagedArea = new()
    {
        Id = Guid.NewGuid(),
        Code = "AREA-3",
        Name = "Area 3"
    };
    private readonly Guid _farmAdministratorId = Guid.NewGuid();
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly List<FarmManagerAssignment> _managerAssignments = new();
    private readonly List<FarmStaffAssignment> _staffAssignments = new();

    public AuthServiceRegisterTests()
    {
        _users.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _users.Setup(r => r.AddAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _areas.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new[] { _area, _freeArea, _secondManagedArea }
                    .FirstOrDefault(area => area.Id == id));
        _areas.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FarmingArea, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<FarmingArea, bool>> predicate, CancellationToken _) =>
                new[] { _area, _freeArea, _secondManagedArea }
                    .AsQueryable().Where(predicate).ToList());
        _areas.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FarmingArea, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<FarmingArea, bool>> predicate, CancellationToken _) =>
                new[] { _area, _secondManagedArea }.AsQueryable().Any(predicate));
        _area.Administrators.Add(new AppUser { Id = _farmAdministratorId, Role = UserRole.FarmAdministrator });
        _secondManagedArea.Administrators.Add(
            new AppUser { Id = _farmAdministratorId, Role = UserRole.FarmAdministrator });
        _managerAssignments.Add(new FarmManagerAssignment
        {
            ManagerUserId = _managerId,
            FarmingAreaId = _area.Id,
            AssignedByUserId = _farmAdministratorId
        });
        var managerRepo = new Mock<IRepository<FarmManagerAssignment>>();
        managerRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>> predicate, CancellationToken _) =>
                _managerAssignments.AsQueryable().FirstOrDefault(predicate));
        managerRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>> predicate,
                CancellationToken _) => _managerAssignments.Where(predicate.Compile()).ToList());
        managerRepo.Setup(r => r.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>> predicate,
                CancellationToken _) => _managerAssignments.Any(predicate.Compile()));
        managerRepo.Setup(r => r.AddAsync(It.IsAny<FarmManagerAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<FarmManagerAssignment, CancellationToken>((item, _) =>
                _managerAssignments.Add(item))
            .Returns(Task.CompletedTask);
        var staffRepo = new Mock<IRepository<FarmStaffAssignment>>();
        staffRepo.Setup(r => r.AddAsync(It.IsAny<FarmStaffAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<FarmStaffAssignment, CancellationToken>((item, _) =>
                _staffAssignments.Add(item))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.Users).Returns(_users.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(_areas.Object);
        _uow.Setup(u => u.FarmManagerAssignments).Returns(managerRepo.Object);
        _uow.Setup(u => u.FarmStaffAssignments).Returns(staffRepo.Object);
        _uow.Setup(u => u.GetNextUserCodeNumberAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _uow.Setup(u => u.OperationLogs).Returns(new Mock<IRepository<OperationLog>>().Object);
    }

    private AuthService Create() => new(_uow.Object, new Mock<IJwtService>().Object);

    private RegisterRequest Request(string role) =>
        new(
            $"user_{Guid.NewGuid():N}",
            $"{Guid.NewGuid():N}@example.com",
            "Secret!12345",
            "Test User",
            role,
            role is "FarmManager" or "FarmStaff" ? _area.Id : null,
            role == "FarmAdministrator" ? new[] { _freeArea.Id } : null,
            role == "FarmStaff" ? _managerId : null);

    [Theory]
    [InlineData("FarmAdministrator")]
    [InlineData("FarmManager")]
    [InlineData("FarmStaff")]
    public async Task SystemAdministrator_CanCreateAnyFarmRole(string role)
    {
        var result = await Create().RegisterAsync(Request(role), UserRole.SystemAdministrator, Guid.NewGuid());
        result.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("FarmManager")]
    [InlineData("FarmStaff")]
    public async Task FarmAdministrator_CanCreateLowerRoles(string role)
    {
        var result = await Create().RegisterAsync(Request(role), UserRole.FarmAdministrator, _farmAdministratorId);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task FarmAdministrator_CannotCreateAnotherFarmAdministrator()
    {
        var act = () => Create().RegisterAsync(Request("FarmAdministrator"), UserRole.FarmAdministrator, _farmAdministratorId);
        await act.Should().ThrowAsync<CrabSenseBE.Application.Common.AppException>();
    }

    [Theory]
    [InlineData("FarmStaff")]
    [InlineData("FarmAdministrator")]
    public async Task ManagerOrStaff_CannotCreateAccounts(string role)
    {
        var act = () => Create().RegisterAsync(Request(role), UserRole.FarmStaff, Guid.NewGuid());
        await act.Should().ThrowAsync<CrabSenseBE.Application.Common.AppException>();
    }

    [Fact]
    public async Task Nobody_CanCreateSystemAdministrator()
    {
        var act = () => Create().RegisterAsync(Request("SystemAdministrator"), UserRole.SystemAdministrator, Guid.NewGuid());
        await act.Should().ThrowAsync<CrabSenseBE.Application.Common.AppException>();
    }

    [Fact]
    public async Task FarmAdministrator_CannotCreateEmployeeForUnmanagedArea()
    {
        var request = Request("FarmStaff") with { FarmingAreaId = Guid.NewGuid() };

        var act = () => Create().RegisterAsync(request, UserRole.FarmAdministrator, _farmAdministratorId);

        await act.Should().ThrowAsync<CrabSenseBE.Application.Common.AppException>();
    }

    [Fact]
    public async Task EmployeeMustBeAssignedToExactlyOneArea()
    {
        var request = Request("FarmStaff") with { FarmingAreaIds = new[] { _area.Id, Guid.NewGuid() } };

        var act = () => Create().RegisterAsync(request, UserRole.SystemAdministrator, Guid.NewGuid());

        await act.Should().ThrowAsync<CrabSenseBE.Application.Common.AppException>();
    }

    [Fact]
    public async Task FarmAdministrator_CreatesManagerWithMultipleManagedAreas()
    {
        var request = Request("FarmManager") with
        {
            FarmingAreaId = null,
            FarmingAreaIds = new[] { _area.Id, _secondManagedArea.Id, _area.Id }
        };

        var result = await Create().RegisterAsync(
            request, UserRole.FarmAdministrator, _farmAdministratorId);

        result.Data!.FarmingAreaId.Should().BeNull();
        result.Data.FarmingAreaIds.Should().BeEquivalentTo(
            new[] { _area.Id, _secondManagedArea.Id });
        _managerAssignments
            .Where(item => item.ManagerUserId == result.Data.Id)
            .Select(item => item.FarmingAreaId)
            .Should().BeEquivalentTo(new[] { _area.Id, _secondManagedArea.Id });
    }

    [Fact]
    public async Task FarmAdministrator_CannotAssignManagerToAnotherAdministratorsArea()
    {
        var request = Request("FarmManager") with
        {
            FarmingAreaId = null,
            FarmingAreaIds = new[] { _area.Id, _freeArea.Id }
        };

        var act = () => Create().RegisterAsync(
            request, UserRole.FarmAdministrator, _farmAdministratorId);

        (await act.Should().ThrowAsync<CrabSenseBE.Application.Common.AppException>())
            .Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Staff_keeps_one_manager_and_one_area_when_manager_has_multiple_areas()
    {
        _managerAssignments.Add(new FarmManagerAssignment
        {
            ManagerUserId = _managerId,
            FarmingAreaId = _secondManagedArea.Id,
            AssignedByUserId = _farmAdministratorId
        });
        var request = Request("FarmStaff") with
        {
            FarmingAreaId = _secondManagedArea.Id,
            ManagerUserId = _managerId
        };

        var result = await Create().RegisterAsync(
            request, UserRole.FarmAdministrator, _farmAdministratorId);

        var assignment = _staffAssignments.Should().ContainSingle(item =>
            item.StaffUserId == result.Data!.Id).Subject;
        assignment.ManagerUserId.Should().Be(_managerId);
        assignment.FarmingAreaId.Should().Be(_secondManagedArea.Id);
    }
}
