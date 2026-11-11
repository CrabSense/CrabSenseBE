using System.Text;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

public sealed class UserMultiAreaPermissionTests : IAsyncLifetime
{
    private PermissionApiFactory _api = null!;
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _adminId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        var users = new Mock<IUserAdminService>();
        users.Setup(service => service.UpdateManagerFarmingAreasAsync(
                _managerId,
                It.IsAny<UpdateManagerFarmingAreasRequest>(),
                UserRole.FarmAdministrator,
                _adminId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<UserDto>.Ok(new UserDto(
                _managerId,
                "manager",
                "manager@example.com",
                "Manager",
                nameof(UserRole.FarmManager),
                true,
                null,
                FarmingAreaIds: [Guid.NewGuid(), Guid.NewGuid()])));
        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(users.Object);
            services.AddSingleton(Mock.Of<IAuthService>());
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Update_manager_areas_requires_authenticated_farm_administrator()
    {
        var body = "{\"farmingAreaIds\":[\""
            + Guid.NewGuid() + "\"]}";
        var path = $"/api/users/{_managerId}/farming-areas";

        var admin = await Send(AppRoles.FarmAdministrator, _adminId, path, body);
        var manager = await Send(
            AppRoles.FarmManager, Guid.NewGuid(), path, body);
        var system = await Send(
            AppRoles.SystemAdministrator, Guid.NewGuid(), path, body);
        var anonymous = await Send(null, null, path, body);

        Assert.Equal(200, (int)admin.StatusCode);
        Assert.Equal(403, (int)manager.StatusCode);
        Assert.Equal(403, (int)system.StatusCode);
        Assert.Equal(401, (int)anonymous.StatusCode);
    }

    private Task<HttpResponseMessage> Send(
        string? role,
        Guid? userId,
        string path,
        string json) =>
        _api.SendAsync(
            role,
            userId,
            HttpMethod.Put,
            path,
            new StringContent(json, Encoding.UTF8, "application/json"));
}
