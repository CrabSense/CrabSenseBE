using System.Net.Http;
using System.Text;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

public sealed class RasPermissionTests : IAsyncLifetime
{
    private static readonly Guid AreaA = Guid.Parse("a11aaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid AreaB = Guid.Parse("b22bbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");
    private static readonly Guid AdminA = Guid.Parse("a11aaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa11");
    private static readonly Guid ManagerA = Guid.Parse("a11aaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa22");
    private static readonly Guid StaffA = Guid.Parse("a11aaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa33");
    private static readonly Guid SystemId = Guid.Parse("a11aaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa44");
    private static readonly Guid WaterId = Guid.Parse("a11aaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa55");
    private static readonly Guid NodeId = Guid.Parse("a11aaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa66");
    private static readonly Guid DeviceA = Guid.Parse("a11aaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa77");
    private static readonly Guid DeviceB = Guid.Parse("b22bbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb88");
    private static readonly Guid FlowId = Guid.Parse("a11aaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa99");

    private PermissionApiFactory _api = null!;
    private int _saves;
    private RasComponent _node = null!;
    private Mock<IEdgeCommandService> _edge = null!;
    private IConfiguration _config = null!;

    public Task InitializeAsync()
    {
        var admin = new AppUser { Id = AdminA, Role = UserRole.FarmAdministrator, IsActive = true, Username = "fa" };
        var manager = new AppUser { Id = ManagerA, Role = UserRole.FarmManager, IsActive = true, Username = "fm" };
        var staff = new AppUser { Id = StaffA, Role = UserRole.FarmStaff, IsActive = true, Username = "fs" };
        var system = new AppUser { Id = SystemId, Role = UserRole.SystemAdministrator, IsActive = true, Username = "sa" };
        var area = new FarmingArea
        {
            Id = AreaA, Code = "AREA-A01", Name = "Khu A", IsActive = true,
            Administrators = { admin }
        };
        var ws = new WaterSystem { Id = WaterId, FarmingAreaId = AreaA, Name = "RAS A", Type = "RAS", IsActive = true };
        var node = new RasComponent
        {
            Id = NodeId,
            WaterSystemId = WaterId,
            Code = "pump",
            Name = "Bơm",
            Type = "PUMP",
            HasRelay = true,
            RelayDeviceId = DeviceA,
            RelayChannel = "1",
            IsOn = false
        };
        _node = node;
        var deviceA = new Device { Id = DeviceA, DeviceCode = "ESP-A", FarmingAreaId = AreaA };
        var deviceB = new Device { Id = DeviceB, DeviceCode = "ESP-B", FarmingAreaId = AreaB };
        var users = new Dictionary<Guid, AppUser>
        {
            [AdminA] = admin, [ManagerA] = manager, [StaffA] = staff, [SystemId] = system
        };

        var areas = new Mock<IRepository<FarmingArea>>();
        areas.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == AreaA ? area : null);
        areas.Setup(r => r.Query()).Returns(() => new[] { area }.AsQueryable());

        var systems = new Mock<IRepository<WaterSystem>>();
        systems.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<WaterSystem, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ws });
        systems.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == WaterId ? ws : null);

        var nodes = new Mock<IRepository<RasComponent>>();
        nodes.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<RasComponent, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { node });
        nodes.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == NodeId ? node : null);

        var flows = new Mock<IRepository<WaterFlow>>();
        flows.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<WaterFlow, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WaterFlow>());
        var sensors = new Mock<IRepository<Sensor>>();
        sensors.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Sensor, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Sensor>());
        var logs = new Mock<IRepository<OperationLog>>();
        logs.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<OperationLog, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OperationLog>());
        logs.Setup(r => r.AddAsync(It.IsAny<OperationLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var devices = new Mock<IRepository<Device>>();
        devices.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == DeviceA ? deviceA : id == DeviceB ? deviceB : null);

        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => users.GetValueOrDefault(id));
        var staffRepo = new Mock<IRepository<FarmStaffAssignment>>();
        staffRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment { StaffUserId = StaffA, FarmingAreaId = AreaA, ManagerUserId = ManagerA });

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.FarmingAreas).Returns(areas.Object);
        uow.Setup(u => u.WaterSystems).Returns(systems.Object);
        uow.Setup(u => u.RasComponents).Returns(nodes.Object);
        uow.Setup(u => u.WaterFlows).Returns(flows.Object);
        uow.Setup(u => u.Sensors).Returns(sensors.Object);
        uow.Setup(u => u.OperationLogs).Returns(logs.Object);
        uow.Setup(u => u.Devices).Returns(devices.Object);
        var commands = new Mock<IRepository<EdgeCommand>>();
        commands.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<EdgeCommand, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EdgeCommand>());
        uow.Setup(u => u.EdgeCommands).Returns(commands.Object);
        uow.Setup(u => u.Users).Returns(userRepo.Object);
        uow.Setup(u => u.FarmStaffAssignments).Returns(staffRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _saves++)
            .ReturnsAsync(1);

        _edge = new Mock<IEdgeCommandService>();
        // Permission tests exercise the hypothetical enabled-control environment.
        // This is an isolated mock, never the deployed configuration.
        _config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["EdgeSync:ControlDisabled"] = "false" }).Build();
        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton<IConfiguration>(_config);
            services.AddSingleton(uow.Object);
            services.AddSingleton(_edge.Object);
            services.AddScoped<IRasFlowService, RasFlowService>();
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Ras_roles_and_area_match_the_matrix()
    {
        _saves = 0;
        using (var read = await _api.SendAsync(AppRoles.FarmAdministrator, AdminA, HttpMethod.Get, $"/api/areas/{AreaA}/ras-flow"))
        {
            Assert.Equal(200, (int)read.StatusCode);
            Assert.Equal(0, _saves);
        }

        var cases = new (string Name, string? Role, Guid? UserId, HttpMethod Method, string Path, string? Json, int Expected)[]
        {
            ("FA đổi sơ đồ đúng khu", AppRoles.FarmAdministrator, AdminA, HttpMethod.Post,
                $"/api/areas/{AreaA}/ras-flow/reorder", "{\"nodeIds\":[\"" + NodeId + "\"]}", 200),
            ("FM thêm node", AppRoles.FarmManager, ManagerA, HttpMethod.Post,
                $"/api/areas/{AreaA}/ras-flow/nodes", "{\"nodeCode\":\"bio\",\"displayLabel\":\"Lọc\"}", 403),
            ("FS xóa đường nước", AppRoles.FarmStaff, StaffA, HttpMethod.Delete,
                $"/api/areas/{AreaA}/ras-flow/flows/{FlowId}", null, 403),
            ("SA gắn relay khu khác", AppRoles.SystemAdministrator, SystemId, HttpMethod.Put,
                $"/api/areas/{AreaA}/ras-flow/nodes/{NodeId}/relay",
                "{\"relayDeviceId\":\"" + DeviceB + "\",\"relayChannel\":\"1\"}", 403),
            ("SA gắn relay đúng khu", AppRoles.SystemAdministrator, SystemId, HttpMethod.Put,
                $"/api/areas/{AreaA}/ras-flow/nodes/{NodeId}/relay",
                "{\"relayDeviceId\":\"" + DeviceA + "\",\"relayChannel\":\"1\"}", 200),
            ("FS gửi lệnh vận hành", AppRoles.FarmStaff, StaffA, HttpMethod.Post,
                $"/api/areas/{AreaA}/ras-flow/nodes/{NodeId}/command", "{\"command\":\"on\"}", 200),
            ("FA không được điều khiển", AppRoles.FarmAdministrator, AdminA, HttpMethod.Post,
                $"/api/areas/{AreaA}/ras-flow/nodes/{NodeId}/command", "{\"command\":\"off\"}", 403)
        };

        var failures = new List<string>();
        foreach (var item in cases)
        {
            using var content = item.Json is null ? null : new StringContent(item.Json, Encoding.UTF8, "application/json");
            using var response = await _api.SendAsync(item.Role, item.UserId, item.Method, item.Path, content);
            var actual = (int)response.StatusCode;
            if (item.Expected == 200 ? actual is 200 or 201 : actual == item.Expected)
                continue;
            failures.Add($"{item.Name}: mong đợi {item.Expected}, nhận {actual}. {await response.Content.ReadAsStringAsync()}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
    [Fact]
    public async Task ManualOn_QueuesWithoutChangingReportedState()
    {
        _saves = 0;
        using var body = new StringContent("{\"command\":\"on\"}", Encoding.UTF8, "application/json");
        using var response = await _api.SendAsync(
            AppRoles.FarmStaff, StaffA, HttpMethod.Post,
            $"/api/areas/{AreaA}/ras-flow/nodes/{NodeId}/command", body);
        Assert.Equal(200, (int)response.StatusCode);
        Assert.False(_node.IsOn);
        Assert.Null(_node.RunStartedAt);
        Assert.Equal(0, _saves);
        _edge.Verify(x => x.EnqueueManualAsync(
            It.Is<CrabSenseBE.Application.DTOs.IoT.EnqueueEdgeCommandRequest>(
                req => req.Command == "on" && req.DeviceCode == "ESP-A"),
            StaffA, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisabledControl_RejectsRasCommandWithoutQueueOrStateMutation()
    {
        _config["EdgeSync:ControlDisabled"] = "true";
        _saves = 0;
        using var body = new StringContent("{\"command\":\"on\"}", Encoding.UTF8, "application/json");
        using var response = await _api.SendAsync(
            AppRoles.FarmStaff, StaffA, HttpMethod.Post,
            $"/api/areas/{AreaA}/ras-flow/nodes/{NodeId}/command", body);
        Assert.Equal(503, (int)response.StatusCode);
        Assert.False(_node.IsOn);
        Assert.Null(_node.RunStartedAt);
        Assert.Equal(0, _saves);
        _edge.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FailedEnqueue_DoesNotSaveReportedState()
    {
        _saves = 0;
        _edge.Setup(x => x.EnqueueManualAsync(
            It.IsAny<CrabSenseBE.Application.DTOs.IoT.EnqueueEdgeCommandRequest>(),
            StaffA, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CrabSenseBE.Application.Common.AppException.Conflict("Device offline."));
        using var body = new StringContent("{\"command\":\"on\"}", Encoding.UTF8, "application/json");
        using var response = await _api.SendAsync(
            AppRoles.FarmStaff, StaffA, HttpMethod.Post,
            $"/api/areas/{AreaA}/ras-flow/nodes/{NodeId}/command", body);
        Assert.Equal(409, (int)response.StatusCode);
        Assert.False(_node.IsOn);
        Assert.Null(_node.RunStartedAt);
        Assert.Equal(0, _saves);
    }

}
