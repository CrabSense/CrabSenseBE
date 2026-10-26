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
/// FarmAdministrator chỉ Manager/Staff, chặn tự vô hiệu hoá.
/// </summary>
public class UserAdminServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IRepository<AppUser>> _users = new();
    private readonly List<AppUser> _store = new();

    public UserAdminServiceTests()
    {
        _users.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _store.ToList());
        _users.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _store.FirstOrDefault(u => u.Id == id));
        _users.Setup(r => r.Update(It.IsAny<AppUser>()));
        _uow.Setup(u => u.Users).Returns(_users.Object);
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
        return user;
    }

    [Fact]
    public async Task SystemAdministrator_SeesEveryRole()
    {
        Add(UserRole.SystemAdministrator);
        Add(UserRole.FarmAdministrator);
        Add(UserRole.FarmManager);
        Add(UserRole.FarmStaff);

        var result = await Create().ListAsync(UserRole.SystemAdministrator);

        result.Data!.Should().HaveCount(4);
    }

    [Fact]
    public async Task FarmAdministrator_SeesOnlyManagerAndStaff()
    {
        Add(UserRole.SystemAdministrator);
        Add(UserRole.FarmAdministrator);
        Add(UserRole.FarmManager);
        Add(UserRole.FarmStaff);

        var result = await Create().ListAsync(UserRole.FarmAdministrator);

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
        var target = Add(UserRole.FarmStaff);

        var result = await Create().SetActiveAsync(
            target.Id, false, UserRole.FarmAdministrator, Guid.NewGuid());

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
            target.Id, "1234", UserRole.FarmAdministrator);

        await act.Should().ThrowAsync<AppException>();
    }

    [Fact]
    public async Task ResetPassword_HashesAndRevokesSessions()
    {
        var target = Add(UserRole.FarmStaff);

        var result = await Create().ResetPasswordAsync(
            target.Id, "NewSecret!123", UserRole.FarmAdministrator);

        result.Success.Should().BeTrue();
        target.PasswordHash.Should().NotBe("hash");
        BCrypt.Net.BCrypt.Verify("NewSecret!123", target.PasswordHash).Should().BeTrue();
        target.RefreshToken.Should().BeNull();
    }
}
