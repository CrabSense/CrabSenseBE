using System.Text;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

/// <summary>Bước 5 — cài đặt trại, dashboard và báo cáo theo khu được phân công.</summary>
public sealed class FarmReportPermissionTests : IAsyncLifetime
{
    private static readonly Guid AreaA = Guid.Parse("aaaaaaa5-0000-0000-0000-00000000000a");
    private static readonly Guid AreaB = Guid.Parse("bbbbbbb5-0000-0000-0000-00000000000b");
    private static readonly Guid RowA = Guid.Parse("aaaaaaa5-0000-0000-0000-0000000000a1");
    private static readonly Guid RowB = Guid.Parse("bbbbbbb5-0000-0000-0000-0000000000b1");
    private static readonly Guid BoxA = Guid.Parse("aaaaaaa5-0000-0000-0000-0000000000a2");
    private static readonly Guid BoxB = Guid.Parse("bbbbbbb5-0000-0000-0000-0000000000b2");
    private static readonly Guid AdminA = Guid.Parse("aaaaaaa5-0000-0000-0000-0000000000f1");
    private static readonly Guid ManagerA = Guid.Parse("ccccccc5-0000-0000-0000-0000000000f3");
    private static readonly Guid StaffA = Guid.Parse("ddddddd5-0000-0000-0000-0000000000f4");
    private static readonly Guid SystemId = Guid.Parse("eeeeeee5-0000-0000-0000-0000000000f5");
    private static readonly Guid LotA = Guid.Parse("aaaaaaa5-0000-0000-0000-0000000000c1");
    private static readonly Guid LotB = Guid.Parse("bbbbbbb5-0000-0000-0000-0000000000c2");

    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        var admin = new AppUser { Id = AdminA, Role = UserRole.FarmAdministrator, IsActive = true, Username = "fa-a" };
        var manager = new AppUser { Id = ManagerA, Role = UserRole.FarmManager, IsActive = true, Username = "fm-a" };
        var staff = new AppUser { Id = StaffA, Role = UserRole.FarmStaff, IsActive = true, Username = "fs-a" };
        var system = new AppUser { Id = SystemId, Role = UserRole.SystemAdministrator, IsActive = true, Username = "sa" };
        var users = new Dictionary<Guid, AppUser>
        {
            [AdminA] = admin, [ManagerA] = manager, [StaffA] = staff, [SystemId] = system
        };
        var areaA = new FarmingArea
        {
            Id = AreaA, Code = "AREA-A01", Name = "Khu A", IsActive = true, Status = FarmStatus.Active,
            Administrators = { admin }
        };
        var areaB = new FarmingArea
        {
            Id = AreaB, Code = "AREA-B01", Name = "Khu B", IsActive = true, Status = FarmStatus.Active
        };
        var rows = new[]
        {
            new FarmingRow { Id = RowA, FarmingAreaId = AreaA, Code = "DAY-A", Name = "Dãy A", IsActive = true },
            new FarmingRow { Id = RowB, FarmingAreaId = AreaB, Code = "DAY-B", Name = "Dãy B", IsActive = true }
        };
        var boxes = new[]
        {
            new Box { Id = BoxA, FarmingRowId = RowA, Code = "BOX-A", Status = "active", IsOccupied = true },
            new Box { Id = BoxB, FarmingRowId = RowB, Code = "BOX-B", Status = "active", IsOccupied = true }
        };
        var ended = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var lots = new[]
        {
            new CrabLot
            {
                Id = LotA, LotCode = "LOT-A", Name = "Lô A", ImportDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Quantity = 2, Status = "Completed", UpdatedAt = ended
            },
            new CrabLot
            {
                Id = LotB, LotCode = "LOT-B", Name = "Lô B", ImportDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
                Quantity = 1, Status = "Completed", UpdatedAt = ended
            }
        };
        var crabs = new[]
        {
            new Crab { Id = Guid.NewGuid(), CrabLotId = LotA, BoxId = BoxA, Code = "CRAB-A", StockedAt = ended, Status = CrabStatus.Alive },
            new Crab { Id = Guid.NewGuid(), CrabLotId = LotB, BoxId = BoxB, Code = "CRAB-B", StockedAt = ended, Status = CrabStatus.Alive, MoltedAt = ended }
        };
        var vouchers = new[]
        {
            new HarvestVoucher
            {
                Id = Guid.NewGuid(), VoucherCode = "HV-A", Status = HarvestStatus.Completed,
                FarmingAreaId = AreaA, HarvestDate = ended, TotalQuantity = 4, TotalWeightKg = 2, CreatedBy = AdminA
            },
            new HarvestVoucher
            {
                Id = Guid.NewGuid(), VoucherCode = "HV-B", Status = HarvestStatus.Completed,
                FarmingAreaId = AreaB, HarvestDate = ended, TotalQuantity = 9, TotalWeightKg = 5, CreatedBy = AdminA
            }
        };
        var settings = new List<FarmSettings>();

        var areaRepo = new Mock<IRepository<FarmingArea>>();
        areaRepo.Setup(r => r.Query()).Returns(() => new[] { areaA, areaB }.AsQueryable());
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

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.Users).Returns(userRepo.Object);
        uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        uow.Setup(u => u.FarmManagerAssignments).Returns(managers.Object);
        uow.Setup(u => u.FarmStaffAssignments).Returns(staffRepo.Object);
        uow.Setup(u => u.FarmingRows).Returns(ListRepo(rows));
        uow.Setup(u => u.Boxes).Returns(ListRepo(boxes));
        uow.Setup(u => u.CrabLots).Returns(ListRepo(lots));
        uow.Setup(u => u.Crabs).Returns(ListRepo(crabs));
        uow.Setup(u => u.HarvestVouchers).Returns(ListRepo(vouchers));
        uow.Setup(u => u.HarvestLines).Returns(ListRepo(Array.Empty<HarvestLine>()));
        uow.Setup(u => u.WaterSystems).Returns(ListRepo(Array.Empty<WaterSystem>()));
        uow.Setup(u => u.Sensors).Returns(ListRepo(Array.Empty<Sensor>()));
        uow.Setup(u => u.Devices).Returns(ListRepo(Array.Empty<Device>()));
        uow.Setup(u => u.Alerts).Returns(ListRepo(Array.Empty<Alert>()));
        uow.Setup(u => u.FrozenLots).Returns(ListRepo(Array.Empty<FrozenLot>()));
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
        uow.Setup(u => u.FarmSettings).Returns(settingRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(uow.Object);
            services.AddScoped<IDashboardService, DashboardService>();
            services.AddScoped<IHarvestReportService, HarvestReportService>();
            services.AddScoped<IInventoryReportService, InventoryReportService>();
            services.AddScoped<ISurvivalRateReportService, SurvivalRateReportService>();
            services.AddScoped<IMoltingReportService, MoltingReportService>();
            services.AddScoped<IOperationalEfficiencyReportService, OperationalEfficiencyReportService>();
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Reports_dashboard_and_settings_follow_the_assigned_area()
    {
        var setting = "{\"key\":\"feed-times\",\"value\":\"2\",\"category\":\"general\"}";
        var harvest = await Send(AppRoles.FarmAdministrator, AdminA, HttpMethod.Get, "/api/reports/harvest", null);
        var otherArea = await Send(AppRoles.FarmAdministrator, AdminA, HttpMethod.Get, $"/api/reports/harvest?farmingAreaId={AreaB}", null);
        var lots = await Send(AppRoles.FarmManager, ManagerA, HttpMethod.Get, "/api/reports/crab-lots/compare", null);
        var staffReport = await Send(AppRoles.FarmStaff, StaffA, HttpMethod.Get, "/api/reports/harvest", null);
        var systemReport = await Send(AppRoles.SystemAdministrator, SystemId, HttpMethod.Get, "/api/reports/inventory", null);
        var saved = await Send(AppRoles.FarmAdministrator, AdminA, HttpMethod.Put, "/api/settings", setting);
        var managerSetting = await Send(AppRoles.FarmManager, ManagerA, HttpMethod.Put, "/api/settings", setting);
        var staffSetting = await Send(AppRoles.FarmStaff, StaffA, HttpMethod.Put, "/api/settings", setting);
        var overview = await Send(AppRoles.FarmAdministrator, AdminA, HttpMethod.Get, "/api/dashboard/overview", null);

        var harvestBody = await harvest.Content.ReadAsStringAsync();
        var lotBody = await lots.Content.ReadAsStringAsync();
        var overviewBody = await overview.Content.ReadAsStringAsync();
        var failures = new List<string>();
        Expect(failures, "FA xem báo cáo khu mình", 200, (int)harvest.StatusCode, harvestBody);
        Expect(failures, "FA xem báo cáo khu khác", 403, (int)otherArea.StatusCode, await otherArea.Content.ReadAsStringAsync());
        Expect(failures, "FM xem báo cáo lô", 200, (int)lots.StatusCode, lotBody);
        Expect(failures, "FS xem báo cáo quản lý", 403, (int)staffReport.StatusCode, await staffReport.Content.ReadAsStringAsync());
        Expect(failures, "SA xem báo cáo nghiệp vụ", 403, (int)systemReport.StatusCode, await systemReport.Content.ReadAsStringAsync());
        Expect(failures, "FA sửa cài đặt trại", 200, (int)saved.StatusCode, await saved.Content.ReadAsStringAsync());
        Expect(failures, "FM sửa cài đặt trại", 403, (int)managerSetting.StatusCode, await managerSetting.Content.ReadAsStringAsync());
        Expect(failures, "FS sửa cài đặt trại", 403, (int)staffSetting.StatusCode, await staffSetting.Content.ReadAsStringAsync());
        Expect(failures, "FA xem dashboard", 200, (int)overview.StatusCode, overviewBody);

        if (!harvestBody.Contains("\"totalQuantity\":4", StringComparison.Ordinal)
            || harvestBody.Contains("\"totalQuantity\":9", StringComparison.Ordinal)
            || harvestBody.Contains("\"totalQuantity\":13", StringComparison.Ordinal))
            failures.Add($"Báo cáo thu hoạch phải chỉ tính khu A. {harvestBody}");
        if (!lotBody.Contains("LOT-A", StringComparison.Ordinal)
            || lotBody.Contains("LOT-B", StringComparison.Ordinal)
            || !lotBody.Contains("unmoltedCount", StringComparison.OrdinalIgnoreCase))
            failures.Add($"Báo cáo lô phải có lô A, ngày kết thúc và cua chưa lột, không có lô B. {lotBody}");
        if (!overviewBody.Contains("\"totalBoxes\":1", StringComparison.Ordinal))
            failures.Add($"Dashboard phải chỉ đếm hộp khu A. {overviewBody}");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IRepository<T> ListRepo<T>(IEnumerable<T> items) where T : class
    {
        var list = items.ToList();
        var repo = new Mock<IRepository<T>>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(list);
        repo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);
        return repo.Object;
    }

    private Task<HttpResponseMessage> Send(string role, Guid userId, HttpMethod method, string path, string? json)
    {
        HttpContent? content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json");
        return _api.SendAsync(role, userId, method, path, content);
    }

    private static void Expect(List<string> failures, string name, int expected, int actual, string body)
    {
        var ok = expected == 200 ? actual is 200 or 201 : actual == expected;
        if (!ok)
            failures.Add($"{name}: mong đợi {expected}, nhận {actual}. {body}");
    }
}
