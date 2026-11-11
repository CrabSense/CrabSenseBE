using System.Text;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

/// <summary>
/// Bước 6 — bộ kiểm thử chung: 401, 403, sai khu, không lộ dữ liệu,
/// không ghi khi bị từ chối, Area → Row → Box, thao tác FM/FS còn dùng được, và audit cấu hình.
/// </summary>
public sealed class PermissionSuiteTests : IAsyncLifetime
{
    private static readonly Guid AreaA = Guid.Parse("aaaaaaa6-0000-0000-0000-00000000000a");
    private static readonly Guid AreaB = Guid.Parse("bbbbbbb6-0000-0000-0000-00000000000b");
    private static readonly Guid RowA = Guid.Parse("aaaaaaa6-0000-0000-0000-0000000000a1");
    private static readonly Guid RowB = Guid.Parse("bbbbbbb6-0000-0000-0000-0000000000b1");
    private static readonly Guid BoxA = Guid.Parse("aaaaaaa6-0000-0000-0000-0000000000a2");
    private static readonly Guid BoxB = Guid.Parse("bbbbbbb6-0000-0000-0000-0000000000b2");
    private static readonly Guid AdminA = Guid.Parse("aaaaaaa6-0000-0000-0000-0000000000f1");
    private static readonly Guid ManagerA = Guid.Parse("ccccccc6-0000-0000-0000-0000000000f3");
    private static readonly Guid StaffA = Guid.Parse("ddddddd6-0000-0000-0000-0000000000f4");
    private static readonly Guid ThresholdB = Guid.Parse("bbbbbbb6-0000-0000-0000-0000000000e1");

    private readonly List<OperationLog> _logs = new();
    private int _saves;
    private FarmingArea _areaB = null!;
    private Box _boxA = null!;
    private AlertThreshold _thresholdB = null!;
    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        var admin = new AppUser { Id = AdminA, Role = UserRole.FarmAdministrator, IsActive = true, Username = "fa-a" };
        var manager = new AppUser { Id = ManagerA, Role = UserRole.FarmManager, IsActive = true, Username = "fm-a" };
        var staff = new AppUser { Id = StaffA, Role = UserRole.FarmStaff, IsActive = true, Username = "fs-a" };
        var users = new Dictionary<Guid, AppUser>
        {
            [AdminA] = admin, [ManagerA] = manager, [StaffA] = staff
        };
        var areaA = new FarmingArea
        {
            Id = AreaA, Code = "AREA-A01", Name = "Khu A", IsActive = true, Status = FarmStatus.Active,
            Administrators = { admin }
        };
        _areaB = new FarmingArea
        {
            Id = AreaB, Code = "AREA-B01", Name = "Khu B", IsActive = true, Status = FarmStatus.Active
        };
        var areas = new[] { areaA, _areaB };
        var rows = new[]
        {
            new FarmingRow { Id = RowA, FarmingAreaId = AreaA, Code = "DAY-A", Name = "Dãy A", IsActive = true },
            new FarmingRow { Id = RowB, FarmingAreaId = AreaB, Code = "DAY-B", Name = "Dãy B", IsActive = true }
        };
        _boxA = new Box { Id = BoxA, FarmingRowId = RowA, Code = "BOX-A", Status = "empty" };
        var boxB = new Box { Id = BoxB, FarmingRowId = RowB, Code = "BOX-B", Status = "empty" };
        var boxes = new[] { _boxA, boxB };
        var thresholds = new List<AlertThreshold>();
        _thresholdB = new AlertThreshold
        {
            Id = ThresholdB, SensorType = "Salinity", MinValue = 10, MaxValue = 25,
            Severity = AlertSeverity.Warning, IsActive = true, FarmingAreaId = AreaB
        };
        thresholds.Add(_thresholdB);
        var settings = new List<FarmSettings>();

        var areaRepo = Repo(areas, area => area.Id);
        areaRepo.Setup(r => r.Query()).Returns(() => areas.AsQueryable());
        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => users.GetValueOrDefault(id));
        var managers = new Mock<IRepository<FarmManagerAssignment>>();
        managers.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FarmManagerAssignment { ManagerUserId = ManagerA, FarmingAreaId = AreaA }
            });
        var staffRepo = new Mock<IRepository<FarmStaffAssignment>>();
        staffRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment { StaffUserId = StaffA, ManagerUserId = ManagerA, FarmingAreaId = AreaA });

        var thresholdRepo = Repo(thresholds, item => item.Id);
        thresholdRepo.Setup(r => r.AddAsync(It.IsAny<AlertThreshold>(), It.IsAny<CancellationToken>()))
            .Callback<AlertThreshold, CancellationToken>((item, _) => thresholds.Add(item))
            .Returns(Task.CompletedTask);

        var settingRepo = new Mock<IRepository<FarmSettings>>();
        settingRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => settings.ToList());
        settingRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmSettings, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<FarmSettings, bool>> predicate, CancellationToken _) =>
                settings.FirstOrDefault(predicate.Compile()));
        settingRepo.Setup(r => r.AddAsync(It.IsAny<FarmSettings>(), It.IsAny<CancellationToken>()))
            .Callback<FarmSettings, CancellationToken>((item, _) => settings.Add(item))
            .Returns(Task.CompletedTask);

        var logs = new Mock<IRepository<OperationLog>>();
        logs.Setup(r => r.AddAsync(It.IsAny<OperationLog>(), It.IsAny<CancellationToken>()))
            .Callback<OperationLog, CancellationToken>((item, _) => _logs.Add(item))
            .Returns(Task.CompletedTask);
        var histories = new Mock<IRepository<BoxStatusHistory>>();
        histories.Setup(r => r.AddAsync(It.IsAny<BoxStatusHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.Users).Returns(userRepo.Object);
        uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        uow.Setup(u => u.FarmManagerAssignments).Returns(managers.Object);
        uow.Setup(u => u.FarmStaffAssignments).Returns(staffRepo.Object);
        uow.Setup(u => u.FarmingRows).Returns(Repo(rows, row => row.Id).Object);
        uow.Setup(u => u.Boxes).Returns(Repo(boxes, box => box.Id).Object);
        uow.Setup(u => u.Crabs).Returns(Repo(Array.Empty<Crab>(), crab => crab.Id).Object);
        uow.Setup(u => u.Alerts).Returns(Repo(Array.Empty<Alert>(), alert => alert.Id).Object);
        uow.Setup(u => u.AlertThresholds).Returns(thresholdRepo.Object);
        uow.Setup(u => u.WaterSystems).Returns(Repo(Array.Empty<WaterSystem>(), item => item.Id).Object);
        uow.Setup(u => u.Sensors).Returns(Repo(Array.Empty<Sensor>(), item => item.Id).Object);
        uow.Setup(u => u.Devices).Returns(Repo(Array.Empty<Device>(), item => item.Id).Object);
        uow.Setup(u => u.FarmSettings).Returns(settingRepo.Object);
        uow.Setup(u => u.OperationLogs).Returns(logs.Object);
        uow.Setup(u => u.BoxStatusHistories).Returns(histories.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _saves++)
            .ReturnsAsync(1);

        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(uow.Object);
            services.AddSingleton(new Mock<IBoxQrService>().Object);
            services.AddSingleton(new Mock<IPublicImageStorage>().Object);
            services.AddSingleton(new Mock<INotificationService>().Object);
            services.AddSingleton(new Mock<IBoxOverviewService>().Object);
            services.AddScoped<IFarmingService, FarmingService>();
            services.AddScoped<IBoxDetailService, BoxDetailService>();
            services.AddScoped<IDashboardService, DashboardService>();
            services.AddScoped<IAlertService, AlertService>();
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Permission_suite_covers_auth_scope_reads_writes_links_and_audit()
    {
        var failures = new List<string>();
        var rename = "{\"name\":\"Khu bị sửa\"}";
        var rowCreate = $"{{\"farmingAreaId\":\"{AreaA}\",\"name\":\"Dãy mới\"}}";
        var status = "{\"status\":\"active\",\"isOccupied\":true}";
        var thresholdUpdate = "{\"minValue\":1,\"maxValue\":2,\"severity\":\"Warning\",\"isActive\":true}";
        var thresholdCreate = $"{{\"sensorType\":\"Temperature\",\"minValue\":24,\"maxValue\":30,\"severity\":\"Warning\",\"farmingAreaId\":\"{AreaA}\"}}";
        var setting = "{\"key\":\"feed-times\",\"value\":\"2\",\"category\":\"general\"}";

        await Expect(failures, "Chưa đăng nhập", 401, null, null, HttpMethod.Get, "/api/farming-areas", null);
        var savesBeforeDenial = _saves;
        var logsBeforeDenial = _logs.Count;

        await Expect(failures, "FM tạo dãy", 403, AppRoles.FarmManager, ManagerA, HttpMethod.Post, "/api/farming-rows", rowCreate);
        await Expect(failures, "FA sửa khu khác", 403, AppRoles.FarmAdministrator, AdminA, HttpMethod.Put, $"/api/farming-areas/{AreaB}", rename);
        await Expect(failures, "FA sửa ngưỡng khu khác", 403, AppRoles.FarmAdministrator, AdminA, HttpMethod.Put, $"/api/alert-thresholds/{ThresholdB}", thresholdUpdate);
        await Expect(failures, "FM sửa cài đặt", 403, AppRoles.FarmManager, ManagerA, HttpMethod.Put, "/api/settings", setting);

        if (_saves != savesBeforeDenial)
            failures.Add("Request bị từ chối vẫn gọi lưu database.");
        if (_logs.Count != logsBeforeDenial)
            failures.Add("Request bị từ chối vẫn ghi audit.");
        if (_areaB.Name != "Khu B" || _thresholdB.MinValue != 10)
            failures.Add("Dữ liệu khu khác bị thay đổi dù request bị từ chối.");

        var boxes = await Send(AppRoles.FarmManager, ManagerA, HttpMethod.Get, "/api/boxes", null);
        var boxBody = await boxes.Content.ReadAsStringAsync();
        await Expect(failures, "FM xem hộp", 200, AppRoles.FarmManager, ManagerA, HttpMethod.Get, "/api/boxes", null);
        if (!boxBody.Contains("BOX-A", StringComparison.Ordinal) || boxBody.Contains("BOX-B", StringComparison.Ordinal))
            failures.Add($"Danh sách hộp lọt dữ liệu ngoài khu. {boxBody}");

        await Expect(failures, "FA xem dãy khu khác", 403, AppRoles.FarmAdministrator, AdminA, HttpMethod.Get, $"/api/farming-rows/{RowB}", null);
        await Expect(failures, "FA xem hộp khu khác", 403, AppRoles.FarmAdministrator, AdminA, HttpMethod.Get, $"/api/boxes/{BoxB}", null);

        var overview = await Send(AppRoles.FarmManager, ManagerA, HttpMethod.Get, "/api/dashboard/overview", null);
        var overviewBody = await overview.Content.ReadAsStringAsync();
        await Expect(failures, "FM xem dashboard", 200, AppRoles.FarmManager, ManagerA, HttpMethod.Get, "/api/dashboard/overview", null);
        if (!overviewBody.Contains("\"totalBoxes\":1", StringComparison.Ordinal))
            failures.Add($"Dashboard của FM gồm hộp ngoài khu. {overviewBody}");

        await Expect(failures, "FS cập nhật trạng thái hộp", 200, AppRoles.FarmStaff, StaffA, HttpMethod.Patch, $"/api/boxes/{BoxA}/status", status);
        if (_boxA.Status != "active")
            failures.Add("Farm Staff không cập nhật được trạng thái vận hành hộp trong khu của mình.");
        if (_logs.Count != 0)
            failures.Add("Cập nhật trạng thái vận hành bị ghi nhầm thành audit cấu hình.");

        await Expect(failures, "FA tạo ngưỡng", 200, AppRoles.FarmAdministrator, AdminA, HttpMethod.Post, "/api/alert-thresholds", thresholdCreate);
        await Expect(failures, "FA sửa cài đặt", 200, AppRoles.FarmAdministrator, AdminA, HttpMethod.Put, "/api/settings", setting);
        if (_logs.All(log => log.Action != "CreateAlertThreshold") || _logs.All(log => log.Action != "UpsertFarmSetting"))
            failures.Add("Thay đổi cấu hình ngưỡng hoặc cài đặt trại không được ghi audit. " + string.Join(", ", _logs.Select(log => log.Action)));
        if (_saves < 2)
            failures.Add("Thay đổi cấu hình hợp lệ không được lưu.");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static Mock<IRepository<T>> Repo<T>(IEnumerable<T> items, Func<T, Guid> id) where T : class
    {
        var list = items as IList<T> ?? items.ToList();
        var repo = new Mock<IRepository<T>>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => list.ToList());
        repo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => list.ToList());
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid key, CancellationToken _) => list.FirstOrDefault(item => id(item) == key));
        return repo;
    }

    private async Task Expect(
        List<string> failures, string name, int expected,
        string? role, Guid? userId, HttpMethod method, string path, string? json)
    {
        using var response = await Send(role, userId, method, path, json);
        var actual = (int)response.StatusCode;
        var ok = expected == 200 ? actual is 200 or 201 or 204 : actual == expected;
        if (!ok)
            failures.Add($"{name}: mong đợi {expected}, nhận {actual}. {await response.Content.ReadAsStringAsync()}");
    }

    private Task<HttpResponseMessage> Send(string? role, Guid? userId, HttpMethod method, string path, string? json)
    {
        HttpContent? content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json");
        return _api.SendAsync(role, userId, method, path, content);
    }
}
