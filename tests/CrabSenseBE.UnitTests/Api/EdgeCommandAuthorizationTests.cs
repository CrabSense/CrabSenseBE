using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Linq.Expressions;
using CrabSenseBE.Api.Authentication;
using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

public sealed class EdgeCommandAuthorizationTests : IAsyncLifetime
{
    private const string Secret =
        "CrabSense_Edge_Command_Test_Secret_At_Least_32_Characters!";
    private readonly Guid _areaA = Guid.NewGuid();
    private readonly Guid _areaB = Guid.NewGuid();
    private readonly Guid _areaC = Guid.NewGuid();
    private readonly AppUser _staff = User(UserRole.FarmStaff, "staff");
    private readonly AppUser _manager = User(UserRole.FarmManager, "manager");
    private readonly List<EdgeCommand> _commands = [];
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private JwtService _jwt = null!;

    public Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = Secret,
                ["Jwt:Issuer"] = "CrabSenseBE.Edge.Tests",
                ["Jwt:Audience"] = "CrabSenseBE.Edge.Clients",
                ["Jwt:ExpiryMinutes"] = "30"
            })
            .Build();
        var deviceA = Device("device-a", _areaA);
        var deviceB = Device("device-b", _areaB);
        var deviceC = Device("device-c", _areaC);
        var users = Repository(_staff, _manager);
        var devices = Repository(deviceA, deviceB, deviceC);
        var managerAssignments = Repository(
            new FarmManagerAssignment
            {
                ManagerUserId = _manager.Id,
                FarmingAreaId = _areaA
            },
            new FarmManagerAssignment
            {
                ManagerUserId = _manager.Id,
                FarmingAreaId = _areaB
            });
        var staffAssignments = Repository(
            new FarmStaffAssignment
            {
                StaffUserId = _staff.Id,
                ManagerUserId = _manager.Id,
                FarmingAreaId = _areaA
            });
        var components = Repository(
            new RasComponent
            {
                RelayDeviceId = deviceA.Id,
                RelayChannel = "pump-1"
            });
        var commandRepo = Repository<EdgeCommand>();
        commandRepo.Setup(repo => repo.AddAsync(
                It.IsAny<EdgeCommand>(), It.IsAny<CancellationToken>()))
            .Callback<EdgeCommand, CancellationToken>((command, _) =>
                _commands.Add(command))
            .Returns(Task.CompletedTask);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Users).Returns(users.Object);
        uow.Setup(x => x.Devices).Returns(devices.Object);
        uow.Setup(x => x.FarmManagerAssignments).Returns(managerAssignments.Object);
        uow.Setup(x => x.FarmStaffAssignments).Returns(staffAssignments.Object);
        uow.Setup(x => x.RasComponents).Returns(components.Object);
        uow.Setup(x => x.EdgeCommands).Returns(commandRepo.Object);
        uow.Setup(x => x.OperationLogs).Returns(Repository<OperationLog>().Object);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddSingleton(uow.Object);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
        builder.Services.AddScoped<IEdgeCommandService, EdgeCommandService>();
        builder.Services.AddCrabSenseJwtAuthentication(configuration);
        builder.Services.AddAuthorization();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(EdgeCommandsController).Assembly);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        _app.StartAsync().GetAwaiter().GetResult();
        _client = _app.GetTestServer().CreateClient();
        _jwt = new JwtService(configuration);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task JwtMappedNameIdentifier_StillEnforcesAreaScope()
    {
        using var deniedStaff = await Enqueue(_staff, "device-b");
        Assert.Equal(HttpStatusCode.Forbidden, deniedStaff.StatusCode);

        using var deniedManager = await Enqueue(_manager, "device-c");
        Assert.Equal(HttpStatusCode.Forbidden, deniedManager.StatusCode);

        using var allowedManager = await Enqueue(_manager, "device-a");
        Assert.Equal(HttpStatusCode.OK, allowedManager.StatusCode);
        Assert.Single(_commands);
        Assert.Equal(_manager.Id, _commands[0].RequestedByUserId);
        Assert.Equal("manual", _commands[0].RequestSource);
    }

    [Fact]
    public async Task UnknownAuthenticatedUser_IsRejectedWithoutCreatingCommand()
    {
        var unknown = User(UserRole.FarmStaff, "unknown");
        using var response = await Enqueue(unknown, "device-a");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_commands);
    }

    private async Task<HttpResponseMessage> Enqueue(AppUser user, string deviceCode)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/edge/commands");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", _jwt.GenerateAccessToken(user).Token);
        request.Content = JsonContent.Create(new
        {
            deviceCode,
            command = "on",
            channel = "pump-1",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        return await _client.SendAsync(request);
    }

    private static AppUser User(UserRole role, string name) => new()
    {
        Id = Guid.NewGuid(),
        Username = name,
        Email = $"{name}@example.test",
        Role = role,
        IsActive = true,
        TokenVersion = 0
    };

    private static Device Device(string code, Guid areaId) => new()
    {
        Id = Guid.NewGuid(),
        DeviceCode = code,
        FarmingAreaId = areaId,
        Status = DeviceStatus.Online,
        EdgeState = "Approved"
    };

    private static Mock<IRepository<T>> Repository<T>(params T[] values)
        where T : class
    {
        var rows = values.ToList();
        var repo = new Mock<IRepository<T>>();
        repo.Setup(x => x.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                rows.FirstOrDefault(row =>
                    (Guid)row.GetType().GetProperty("Id")!.GetValue(row)! == id));
        repo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                rows.FirstOrDefault(predicate.Compile()));
        repo.Setup(x => x.FindAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                rows.Where(predicate.Compile()).ToArray());
        repo.Setup(x => x.AnyAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                rows.Any(predicate.Compile()));
        return repo;
    }
}
