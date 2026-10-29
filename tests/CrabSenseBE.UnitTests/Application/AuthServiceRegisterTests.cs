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
        OwnerId = Guid.NewGuid(),
        Code = "AREA-1",
        Name = "Area 1"
    };
    private readonly Guid _farmAdministratorId = Guid.NewGuid();

    public AuthServiceRegisterTests()
    {
        _users.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _users.Setup(r => r.AddAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _areas.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == _area.Id ? _area : null);
        _areas.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FarmingArea, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<FarmingArea, bool>> predicate, CancellationToken _) =>
                new[] { _area }.AsQueryable().Where(predicate).ToList());
        _areas.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FarmingArea, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<FarmingArea, bool>> predicate, CancellationToken _) =>
                new[] { _area }.AsQueryable().Any(predicate));
        _area.Administrators.Add(new AppUser { Id = _farmAdministratorId, Role = UserRole.FarmAdministrator });
        _uow.Setup(u => u.Users).Returns(_users.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(_areas.Object);
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
            role == "FarmAdministrator" ? new[] { _area.Id } : null);

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
}
