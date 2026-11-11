using System.Text;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Options;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

/// <summary>Bước 4 — ngưỡng và cảnh báo gắn với khu được phân công.</summary>
public sealed class AlertThresholdPermissionTests : IAsyncLifetime
{
    private static readonly Guid AreaA = Guid.Parse("aaaaaaa4-0000-0000-0000-00000000000a");
    private static readonly Guid AreaB = Guid.Parse("bbbbbbb4-0000-0000-0000-00000000000b");
    private static readonly Guid AdminA = Guid.Parse("aaaaaaa4-0000-0000-0000-0000000000f1");
    private static readonly Guid ManagerA = Guid.Parse("ccccccc4-0000-0000-0000-0000000000f3");
    private static readonly Guid StaffA = Guid.Parse("ddddddd4-0000-0000-0000-0000000000f4");
    private static readonly Guid SystemId = Guid.Parse("eeeeeee4-0000-0000-0000-0000000000f5");
    private static readonly Guid ThresholdA = Guid.Parse("aaaaaaa4-0000-0000-0000-000000000001");
    private static readonly Guid ThresholdB = Guid.Parse("bbbbbbb4-0000-0000-0000-0000000000b9");
    private static readonly Guid SensorA = Guid.Parse("aaaaaaa4-0000-0000-0000-0000000000c1");
    private static readonly Guid SensorB = Guid.Parse("bbbbbbb4-0000-0000-0000-0000000000c2");
    private static readonly Guid SystemA = Guid.Parse("aaaaaaa4-0000-0000-0000-0000000000d1");
    private static readonly Guid SystemB = Guid.Parse("bbbbbbb4-0000-0000-0000-0000000000d2");
    private static readonly Guid AlertA = Guid.Parse("aaaaaaa4-0000-0000-0000-0000000000e1");
    private static readonly Guid AlertB = Guid.Parse("bbbbbbb4-0000-0000-0000-0000000000e2");

    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        var adminA = new AppUser { Id = AdminA, Role = UserRole.FarmAdministrator, IsActive = true, Username = "fa-a" };
        var manager = new AppUser { Id = ManagerA, Role = UserRole.FarmManager, IsActive = true, Username = "fm-a" };
        var staff = new AppUser { Id = StaffA, Role = UserRole.FarmStaff, IsActive = true, Username = "fs-a" };
        var system = new AppUser { Id = SystemId, Role = UserRole.SystemAdministrator, IsActive = true, Username = "sa" };
        var areaA = new FarmingArea
        {
            Id = AreaA, Code = "AREA-A01", Name = "Khu A", IsActive = true, Status = FarmStatus.Active,
            Administrators = { adminA }
        };
        var areaB = new FarmingArea
        {
            Id = AreaB, Code = "AREA-B01", Name = "Khu B", IsActive = true, Status = FarmStatus.Active
        };
        var users = new Dictionary<Guid, AppUser>
        {
            [AdminA] = adminA, [ManagerA] = manager, [StaffA] = staff, [SystemId] = system
        };
        var areas = new[] { areaA, areaB };
        var thresholds = new List<AlertThreshold>
        {
            new()
            {
                Id = ThresholdA, SensorType = "pH", MinValue = 6.5m, MaxValue = 8.5m,
                Severity = AlertSeverity.Warning, IsActive = true, FarmingAreaId = AreaA
            },
            new()
            {
                Id = ThresholdB, SensorType = "Salinity", MinValue = 10m, MaxValue = 25m,
                Severity = AlertSeverity.Warning, IsActive = true, FarmingAreaId = AreaB
            }
        };
        var sensors = new[]
        {
            new Sensor { Id = SensorA, WaterSystemId = SystemA, SensorCode = "PH-A", SensorType = "pH" },
            new Sensor { Id = SensorB, WaterSystemId = SystemB, SensorCode = "SAL-B", SensorType = "Salinity" }
        };
        var water = new[]
        {
            new WaterSystem { Id = SystemA, FarmingAreaId = AreaA, Name = "RAS A", IsActive = true },
            new WaterSystem { Id = SystemB, FarmingAreaId = AreaB, Name = "RAS B", IsActive = true }
        };
        var alerts = new[]
        {
            new Alert
            {
                Id = AlertA, SensorId = SensorA, Message = "pH khu A",
                Severity = AlertSeverity.Warning, Status = AlertStatus.Active
            },
            new Alert
            {
                Id = AlertB, SensorId = SensorB, Message = "Salinity khu B",
                Severity = AlertSeverity.Warning, Status = AlertStatus.Active
            }
        };

        var areaRepo = new Mock<IRepository<FarmingArea>>();
        areaRepo.Setup(r => r.Query()).Returns(() => areas.AsQueryable());
        areaRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FarmingArea, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(areas);

        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => users.GetValueOrDefault(id));

        var managers = new Mock<IRepository<FarmManagerAssignment>>();
        managers.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmManagerAssignment { ManagerUserId = ManagerA, FarmingAreaId = AreaA });
        var staffRepo = new Mock<IRepository<FarmStaffAssignment>>();
        staffRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment { StaffUserId = StaffA, ManagerUserId = ManagerA, FarmingAreaId = AreaA });

        var thresholdRepo = new Mock<IRepository<AlertThreshold>>();
        thresholdRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => thresholds.ToList());
        thresholdRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => thresholds.FirstOrDefault(item => item.Id == id));
        thresholdRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<AlertThreshold, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => thresholds.ToList());
        thresholdRepo.Setup(r => r.AddAsync(It.IsAny<AlertThreshold>(), It.IsAny<CancellationToken>()))
            .Callback<AlertThreshold, CancellationToken>((entity, _) => thresholds.Add(entity))
            .Returns(Task.CompletedTask);

        var alertRepo = new Mock<IRepository<Alert>>();
        alertRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Alert, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(alerts);
        alertRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => alerts.FirstOrDefault(item => item.Id == id));

        var sensorRepo = new Mock<IRepository<Sensor>>();
        sensorRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Sensor, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sensors);

        var waterRepo = new Mock<IRepository<WaterSystem>>();
        waterRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<WaterSystem, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(water);
        waterRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => water.FirstOrDefault(item => item.Id == id));

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        uow.Setup(u => u.Users).Returns(userRepo.Object);
        uow.Setup(u => u.FarmManagerAssignments).Returns(managers.Object);
        uow.Setup(u => u.FarmStaffAssignments).Returns(staffRepo.Object);
        uow.Setup(u => u.AlertThresholds).Returns(thresholdRepo.Object);
        uow.Setup(u => u.Alerts).Returns(alertRepo.Object);
        uow.Setup(u => u.Sensors).Returns(sensorRepo.Object);
        uow.Setup(u => u.WaterSystems).Returns(waterRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(uow.Object);
            services.AddSingleton(new Mock<INotificationService>().Object);
            services.AddScoped<IAlertService, AlertService>();
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Thresholds_and_alerts_stay_inside_the_assigned_area()
    {
        var create = $"{{\"sensorType\":\"Temperature\",\"minValue\":24,\"maxValue\":30,\"severity\":\"Warning\",\"farmingAreaId\":\"{AreaA}\"}}";
        var update = "{\"minValue\":7,\"maxValue\":8.2,\"severity\":\"Warning\",\"isActive\":true}";

        var created = await Send(AppRoles.FarmAdministrator, AdminA, HttpMethod.Post, "/api/alert-thresholds", create);
        var crossArea = await Send(AppRoles.FarmAdministrator, AdminA, HttpMethod.Put, $"/api/alert-thresholds/{ThresholdB}", update);
        var managerEdit = await Send(AppRoles.FarmManager, ManagerA, HttpMethod.Put, $"/api/alert-thresholds/{ThresholdA}", update);
        var staffDelete = await Send(AppRoles.FarmStaff, StaffA, HttpMethod.Delete, $"/api/alert-thresholds/{ThresholdA}", null);
        var managerView = await Send(AppRoles.FarmManager, ManagerA, HttpMethod.Get, "/api/alert-thresholds", null);
        var staffView = await Send(AppRoles.FarmStaff, StaffA, HttpMethod.Get, "/api/alerts", null);
        var managerAlerts = await Send(AppRoles.FarmManager, ManagerA, HttpMethod.Get, "/api/alerts", null);
        var systemEdit = await Send(AppRoles.SystemAdministrator, SystemId, HttpMethod.Put, $"/api/alert-thresholds/{ThresholdA}", update);

        var managerBody = await managerView.Content.ReadAsStringAsync();
        var staffBody = await staffView.Content.ReadAsStringAsync();
        var failures = new List<string>();
        Expect(failures, "FA tạo ngưỡng đúng khu", 200, (int)created.StatusCode, await created.Content.ReadAsStringAsync());
        Expect(failures, "FA sửa ngưỡng khu khác", 403, (int)crossArea.StatusCode, await crossArea.Content.ReadAsStringAsync());
        Expect(failures, "FM sửa ngưỡng", 403, (int)managerEdit.StatusCode, await managerEdit.Content.ReadAsStringAsync());
        Expect(failures, "FS xóa ngưỡng", 403, (int)staffDelete.StatusCode, await staffDelete.Content.ReadAsStringAsync());
        Expect(failures, "FM xem ngưỡng", 200, (int)managerView.StatusCode, managerBody);
        Expect(failures, "FS xem cảnh báo", 200, (int)staffView.StatusCode, staffBody);
        Expect(failures, "FM xem cảnh báo", 200, (int)managerAlerts.StatusCode, await managerAlerts.Content.ReadAsStringAsync());
        Expect(failures, "SA sửa ngưỡng nuôi", 403, (int)systemEdit.StatusCode, await systemEdit.Content.ReadAsStringAsync());
        if (!managerBody.Contains("pH", StringComparison.Ordinal) || managerBody.Contains("Salinity", StringComparison.Ordinal))
            failures.Add("FM phải thấy ngưỡng pH của khu A và không thấy Salinity của khu B.");
        if (!staffBody.Contains("pH khu A", StringComparison.Ordinal) || staffBody.Contains("Salinity khu B", StringComparison.Ordinal))
            failures.Add("FS phải thấy cảnh báo khu A và không thấy cảnh báo khu B.");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task Environmental_rule_rejects_channel_template_and_keeps_farm_switch()
    {
        var rule = new NotificationRule
        {
            TypeCode = "sensor_threshold",
            DisplayName = "Ngưỡng cảm biến",
            IsEnabled = true,
            ChannelCodes = "in_app"
        };
        var rules = new Mock<IRepository<NotificationRule>>();
        rules.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<NotificationRule, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { rule });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.NotificationRules).Returns(rules.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var service = new NotificationService(
            uow.Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<ILogger<NotificationService>>().Object,
            Options.Create(new FcmOptions()),
            Options.Create(new NotificationOptions()));

        var denied = await Assert.ThrowsAsync<AppException>(() => service.UpdateRuleAsync(
            "sensor_threshold",
            new UpdateNotificationRuleRequest(ChannelCodes: "telegram", TitleTemplate: "Mẫu", BodyTemplate: "Nội dung")));
        Assert.Equal(403, denied.StatusCode);

        var updated = await service.UpdateRuleAsync(
            "sensor_threshold",
            new UpdateNotificationRuleRequest(IsEnabled: false));
        Assert.False(rule.IsEnabled);
        Assert.True(updated.Success);
    }

    private Task<HttpResponseMessage> Send(string role, Guid userId, HttpMethod method, string path, string? json)
    {
        HttpContent? content = json is null
            ? null
            : new StringContent(json, Encoding.UTF8, "application/json");
        return _api.SendAsync(role, userId, method, path, content);
    }

    private static void Expect(List<string> failures, string name, int expected, int actual, string body)
    {
        var ok = expected == 200 ? actual is 200 or 201 : actual == expected;
        if (!ok)
            failures.Add($"{name}: mong đợi {expected}, nhận {actual}. {body}");
    }
}
