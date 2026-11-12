using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrabSenseBE.UnitTests.Application;

public class AuthServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IJwtService> _jwtMock = new();

    public AuthServiceTests()
    {
        var managers = new Mock<IRepository<FarmManagerAssignment>>();
        managers.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FarmManagerAssignment>());
        var staff = new Mock<IRepository<FarmStaffAssignment>>();
        staff.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FarmStaffAssignment?)null);
        _uowMock.Setup(u => u.FarmManagerAssignments).Returns(managers.Object);
        _uowMock.Setup(u => u.FarmStaffAssignments).Returns(staff.Object);
    }

    private AuthService CreateService() => new(_uowMock.Object, _jwtMock.Object);

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        // Arrange
        var passwordHash = BCrypt.Net.BCrypt.HashPassword("password123");
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = passwordHash,
            FullName = "Test User",
            Role = UserRole.FarmStaff,
            IsActive = true
        };

        var userRepoMock = new Mock<IRepository<AppUser>>();
        userRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        userRepoMock.Setup(r => r.Update(It.IsAny<AppUser>()));

        _uowMock.Setup(u => u.Users).Returns(userRepoMock.Object);
        _uowMock.Setup(u => u.OperationLogs).Returns(new Mock<IRepository<OperationLog>>().Object);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        _jwtMock.Setup(j => j.GenerateAccessToken(It.IsAny<AppUser>()))
            .Returns(new AccessTokenResult("access_token", expiresAt));
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("refresh_token");

        var service = CreateService();

        // Act
        var result = await service.LoginAsync(new LoginRequest("testuser", "password123"));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.AccessToken.Should().Be("access_token");
        result.Data.ExpiresAt.Should().Be(expiresAt);
        user.RefreshToken.Should().BeNull();
        user.RefreshTokenHash.Should().Be(
            Hash("refresh_token"));
        user.RefreshTokenHash.Should().NotBe("refresh_token");
    }

    [Fact]
    public async Task Login_WithWrongPassword_ThrowsUnauthorized()
    {
        // Arrange
        var user = new AppUser
        {
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpassword"),
            IsActive = true
        };

        var userRepoMock = new Mock<IRepository<AppUser>>();
        userRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), default))
            .ReturnsAsync(user);

        _uowMock.Setup(u => u.Users).Returns(userRepoMock.Object);
        _uowMock.Setup(u => u.OperationLogs).Returns(new Mock<IRepository<OperationLog>>().Object);

        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<AppException>(() =>
            service.LoginAsync(new LoginRequest("testuser", "wrongpassword")));
    }

    [Fact]
    public async Task Login_WhenManagedAreasNotLoaded_DoesNotThrow500()
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "owner",
            Email = "owner@crabsense.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123"),
            FullName = "Chủ trại",
            Role = UserRole.FarmAdministrator,
            IsActive = true,
            ManagedAreas = null!
        };

        var userRepoMock = new Mock<IRepository<AppUser>>();
        userRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        userRepoMock.Setup(r => r.Update(It.IsAny<AppUser>()));
        _uowMock.Setup(u => u.Users).Returns(userRepoMock.Object);
        _uowMock.Setup(u => u.OperationLogs).Returns(new Mock<IRepository<OperationLog>>().Object);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _jwtMock.Setup(j => j.GenerateAccessToken(It.IsAny<AppUser>()))
            .Returns(new AccessTokenResult(
                "access_token", DateTime.UtcNow.AddMinutes(30)));
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("refresh_token");

        var result = await CreateService().LoginAsync(new LoginRequest("owner", "Owner@123"));

        result.Success.Should().BeTrue();
        result.Data!.User.Username.Should().Be("owner");
    }

    [Fact]
    public async Task Logout_and_change_password_revoke_access_and_refresh_sessions()
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "staff",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword123!"),
            RefreshToken = "refresh",
            RefreshTokenExpiry = DateTime.UtcNow.AddDays(1),
            TokenVersion = 4
        };
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(
                user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        users.Setup(repo => repo.Update(user));
        _uowMock.Setup(unit => unit.Users).Returns(users.Object);
        _uowMock.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var service = CreateService();

        await service.LogoutAsync(user.Id);
        user.TokenVersion.Should().Be(5);
        user.RefreshToken.Should().BeNull();

        user.RefreshToken = "refresh-2";
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(1);
        await service.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest("OldPassword123!", "NewPassword123!"));

        user.TokenVersion.Should().Be(6);
        user.RefreshToken.Should().BeNull();
        user.RefreshTokenExpiry.Should().BeNull();
        BCrypt.Net.BCrypt.Verify("NewPassword123!", user.PasswordHash)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_token_is_rotated_and_previous_value_cannot_be_reused()
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "manager",
            Email = "manager@example.test",
            Role = UserRole.FarmManager,
            IsActive = true,
            RefreshToken = "old-refresh",
            RefreshTokenExpiry = DateTime.UtcNow.AddDays(1)
        };
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<AppUser, bool>> predicate,
                CancellationToken _) =>
                new[] { user }.AsQueryable().FirstOrDefault(predicate));
        users.Setup(repo => repo.Update(user));
        _uowMock.Setup(unit => unit.Users).Returns(users.Object);
        _uowMock.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var accessExpiresAt = DateTime.UtcNow.AddMinutes(15);
        _jwtMock.Setup(jwt => jwt.GenerateAccessToken(user))
            .Returns(new AccessTokenResult("access", accessExpiresAt));
        _jwtMock.SetupSequence(jwt => jwt.GenerateRefreshToken())
            .Returns("new-refresh")
            .Returns("another-refresh");
        var service = CreateService();

        var result = await service.RefreshTokenAsync(
            new RefreshTokenRequest("old-refresh"));
        result.Data!.RefreshToken.Should().Be("new-refresh");
        result.Data.ExpiresAt.Should().Be(accessExpiresAt);
        user.RefreshToken.Should().BeNull();
        user.RefreshTokenHash.Should().Be(
            Hash("new-refresh"));

        var reuse = () => service.RefreshTokenAsync(
            new RefreshTokenRequest("old-refresh"));
        (await reuse.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Concurrent_refresh_loser_is_rejected_as_replay()
    {
        const string oldRefresh = "old-refresh";
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "manager",
            IsActive = true,
            RefreshTokenHash = Hash(oldRefresh),
            RefreshTokenExpiry = DateTime.UtcNow.AddDays(1),
            RefreshTokenVersion = 3
        };
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _uowMock.Setup(unit => unit.Users).Returns(users.Object);
        _uowMock.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());
        _jwtMock.Setup(jwt => jwt.GenerateAccessToken(user))
            .Returns(new AccessTokenResult(
                "discarded-access", DateTime.UtcNow.AddMinutes(15)));
        _jwtMock.Setup(jwt => jwt.GenerateRefreshToken())
            .Returns("discarded-refresh");

        var act = () => CreateService().RefreshTokenAsync(
            new RefreshTokenRequest(oldRefresh));

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Login_succeeds_again_only_after_account_is_reactivated()
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "reactivated",
            Email = "reactivated@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            Role = UserRole.FarmStaff,
            IsActive = false,
            TokenVersion = 1
        };
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<AppUser, bool>> predicate,
                CancellationToken _) =>
                new[] { user }.AsQueryable().FirstOrDefault(predicate));
        users.Setup(repo => repo.Update(user));
        var logs = new Mock<IRepository<OperationLog>>();
        logs.Setup(repo => repo.AddAsync(
                It.IsAny<OperationLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _uowMock.Setup(unit => unit.Users).Returns(users.Object);
        _uowMock.Setup(unit => unit.OperationLogs).Returns(logs.Object);
        _uowMock.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _jwtMock.Setup(jwt => jwt.GenerateAccessToken(user))
            .Returns(new AccessTokenResult(
                "access", DateTime.UtcNow.AddMinutes(30)));
        _jwtMock.Setup(jwt => jwt.GenerateRefreshToken()).Returns("refresh");
        var service = CreateService();

        var lockedLogin = () => service.LoginAsync(
            new LoginRequest(user.Username, "Password123!"));
        (await lockedLogin.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(401);

        user.IsActive = true;
        var result = await service.LoginAsync(
            new LoginRequest(user.Username, "Password123!"));

        result.Success.Should().BeTrue();
        result.Data!.AccessToken.Should().Be("access");
    }

    [Fact]
    public async Task Login_uses_configured_refresh_lifetime()
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(), Username = "policy-user", Role = UserRole.FarmStaff,
            IsActive = true, PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!")
        };
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(x => x.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _uowMock.Setup(x => x.Users).Returns(users.Object);
        _uowMock.Setup(x => x.OperationLogs).Returns(new Mock<IRepository<OperationLog>>().Object);
        _jwtMock.Setup(x => x.GenerateAccessToken(user)).Returns(new AccessTokenResult("access", DateTime.UtcNow.AddMinutes(15)));
        _jwtMock.Setup(x => x.GenerateRefreshToken()).Returns("refresh");
        var platform = new Mock<CrabSenseBE.Application.Interfaces.ISystemPlatformService>();
        platform.Setup(x => x.GetPolicyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<CrabSenseBE.Application.DTOs.SystemPlatform.SecurityPolicyDto>.Ok(
                new CrabSenseBE.Application.DTOs.SystemPlatform.SecurityPolicyDto(7, 3, true)));
        var before = DateTime.UtcNow;
        await new AuthService(_uowMock.Object, _jwtMock.Object, platform.Object)
            .LoginAsync(new LoginRequest(user.Username, "Password123!"));
        user.RefreshTokenExpiry!.Value.Should().BeOnOrAfter(before.AddDays(7));
        user.RefreshTokenExpiry.Value.Should().BeBefore(DateTime.UtcNow.AddDays(7).AddSeconds(1));
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
