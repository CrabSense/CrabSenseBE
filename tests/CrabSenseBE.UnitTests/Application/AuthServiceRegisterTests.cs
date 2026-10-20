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
/// FarmAdministrator chỉ tạo được Manager/Staff, role khác bị chặn.
/// </summary>
public class AuthServiceRegisterTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IRepository<AppUser>> _users = new();

    public AuthServiceRegisterTests()
    {
        _users.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _users.Setup(r => r.AddAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.Users).Returns(_users.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _uow.Setup(u => u.OperationLogs).Returns(new Mock<IRepository<OperationLog>>().Object);
    }

    private AuthService Create() => new(_uow.Object, new Mock<IJwtService>().Object);

    private static RegisterRequest Request(string role) =>
        new($"user_{Guid.NewGuid():N}", $"{Guid.NewGuid():N}@example.com", "Secret!12345", "Test User", role);

    [Theory]
    [InlineData("FarmAdministrator")]
    [InlineData("FarmManager")]
    [InlineData("FarmStaff")]
    public async Task SystemAdministrator_CanCreateAnyFarmRole(string role)
    {
        var result = await Create().RegisterAsync(Request(role), UserRole.SystemAdministrator);
        result.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("FarmManager")]
    [InlineData("FarmStaff")]
    public async Task FarmAdministrator_CanCreateLowerRoles(string role)
    {
        var result = await Create().RegisterAsync(Request(role), UserRole.FarmAdministrator);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task FarmAdministrator_CannotCreateAnotherFarmAdministrator()
    {
        var act = () => Create().RegisterAsync(Request("FarmAdministrator"), UserRole.FarmAdministrator);
        await act.Should().ThrowAsync<CrabSenseBE.Application.Common.AppException>();
    }

    [Theory]
    [InlineData("FarmStaff")]
    [InlineData("FarmAdministrator")]
    public async Task ManagerOrStaff_CannotCreateAccounts(string role)
    {
        var act = () => Create().RegisterAsync(Request(role), UserRole.FarmStaff);
        await act.Should().ThrowAsync<CrabSenseBE.Application.Common.AppException>();
    }

    [Fact]
    public async Task Nobody_CanCreateSystemAdministrator()
    {
        var act = () => Create().RegisterAsync(Request("SystemAdministrator"), UserRole.SystemAdministrator);
        await act.Should().ThrowAsync<CrabSenseBE.Application.Common.AppException>();
    }
}
