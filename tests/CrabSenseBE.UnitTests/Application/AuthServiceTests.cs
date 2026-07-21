using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Moq;
using FluentAssertions;
using Xunit;

namespace CrabSenseBE.UnitTests.Application;

public class AuthServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IJwtService> _jwtMock = new();

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
            Role = UserRole.Staff,
            IsActive = true
        };

        var userRepoMock = new Mock<IRepository<AppUser>>();
        userRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        userRepoMock.Setup(r => r.Update(It.IsAny<AppUser>()));

        _uowMock.Setup(u => u.Users).Returns(userRepoMock.Object);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _jwtMock.Setup(j => j.GenerateAccessToken(It.IsAny<AppUser>())).Returns("access_token");
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("refresh_token");

        var service = CreateService();

        // Act
        var result = await service.LoginAsync(new LoginRequest("testuser", "password123"));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.AccessToken.Should().Be("access_token");
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

        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<AppException>(() =>
            service.LoginAsync(new LoginRequest("testuser", "wrongpassword")));
    }
}
