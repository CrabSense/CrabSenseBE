using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Api.Middleware;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

/// <summary>Đọc role từ header X-Test-User: Role|UserId. Không có header = chưa đăng nhập.</summary>
public sealed class TestRoleAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string AuthScheme = "Test";
    public const string Header = "X-Test-User";

    public TestRoleAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var raw) || string.IsNullOrWhiteSpace(raw))
            return Task.FromResult(AuthenticateResult.NoResult());

        var parts = raw.ToString().Split('|', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[1], out _))
            return Task.FromResult(AuthenticateResult.Fail("X-Test-User phải là Role|UserId."));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parts[1]),
            new Claim("sub", parts[1]),
            new Claim(ClaimTypes.Role, parts[0])
        };
        var identity = new ClaimsIdentity(claims, AuthScheme);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), AuthScheme)));
    }
}

public sealed class PermissionApiFactory : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }

    private PermissionApiFactory(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    public static PermissionApiFactory Create(Action<IServiceCollection> configure)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = "CrabSenseBE.Api"
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
        builder.Services
            .AddAuthentication(TestRoleAuthHandler.AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestRoleAuthHandler>(TestRoleAuthHandler.AuthScheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(FarmingRowsController).Assembly);
        configure(builder.Services);

        var app = builder.Build();
        app.UseMiddleware<ExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.StartAsync().GetAwaiter().GetResult();
        return new PermissionApiFactory(app, app.GetTestServer().CreateClient());
    }

    public async Task<HttpResponseMessage> SendAsync(
        string? role, Guid? userId, HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (role is not null && userId is Guid id)
            request.Headers.TryAddWithoutValidation(TestRoleAuthHandler.Header, $"{role}|{id}");
        if (content is not null)
            request.Content = content;
        return await Client.SendAsync(request);
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        return _app.DisposeAsync();
    }
}

public sealed class ChangedApiPermissionTests : IAsyncLifetime
{
    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ManagerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid StaffId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid SystemId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid AreaId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid RowId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid BoxId = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid AlertId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TechnicalAlertId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThresholdId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(StubFarming());
            services.AddSingleton(StubAlerts());
            services.AddSingleton(StubNotifications());
            services.AddSingleton(StubDashboard());
            services.AddSingleton(new Mock<IBoxOverviewService>().Object);
            services.AddSingleton(new Mock<IBoxDetailService>().Object);
            services.AddSingleton(new Mock<ICrabLotComparisonReportService>().Object);
            services.AddSingleton(new Mock<IHarvestReportService>().Object);
            services.AddSingleton(new Mock<IInventoryReportService>().Object);
            services.AddSingleton(new Mock<ISurvivalRateReportService>().Object);
            services.AddSingleton(new Mock<IMoltingReportService>().Object);
            services.AddSingleton(new Mock<IOperationalEfficiencyReportService>().Object);
            services.AddSingleton(StubUnitOfWork());
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Changed_endpoints_allow_the_right_role_and_reject_the_others()
    {
        var failures = new List<string>();
        foreach (var call in Cases())
        {
            await Check(failures, call, AppRoles.FarmAdministrator, AdminId, call.Admin);
            await Check(failures, call, AppRoles.FarmManager, ManagerId, call.Manager);
            await Check(failures, call, AppRoles.FarmStaff, StaffId, call.Staff);
            await Check(failures, call, AppRoles.SystemAdministrator, SystemId, call.System);
            await Check(failures, call, null, null, call.Anonymous);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Sync_business_data_excludes_platform_and_queue_is_platform_only()
    {
        var pullRoles = typeof(SyncQueueStubController)
            .GetMethod(nameof(SyncQueueStubController.PullEntities))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute => attribute.Roles)
            .Single(role => role is not null);
        Assert.Equal(AppRoles.FarmPortal, pullRoles);
        Assert.DoesNotContain(AppRoles.SystemAdministrator, pullRoles);

        var queueRoles = typeof(SyncQueueStubController)
            .GetMethod(nameof(SyncQueueStubController.GetSyncQueue))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute => attribute.Roles)
            .Single(role => role is not null);
        Assert.Equal(AppRoles.Platform, queueRoles);

        var v1Roles = typeof(SyncV1StubController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute => attribute.Roles)
            .Single(role => role is not null);
        Assert.Equal(AppRoles.Platform, v1Roles);
    }

    private async Task Check(List<string> failures, ApiCall call, string? role, Guid? userId, int expected)
    {
        using var content = call.Multipart ? AvatarContent() : JsonContent(call.Json);
        using var response = await _api.SendAsync(role, userId, call.Method, call.Path, content);
        var actual = (int)response.StatusCode;
        if (Matches(actual, expected))
            return;
        var body = await response.Content.ReadAsStringAsync();
        var who = role ?? "Chưa đăng nhập";
        failures.Add($"{call.Method} {call.Path} / {who}: mong đợi {expected}, nhận {actual}. {body}");
    }

    private static bool Matches(int actual, int expected) =>
        expected == (int)HttpStatusCode.OK
            ? actual is (int)HttpStatusCode.OK
                or (int)HttpStatusCode.Created
                or (int)HttpStatusCode.NoContent
            : actual == expected;

    private static HttpContent? JsonContent(string? json) =>
        json is null
            ? null
            : new StringContent(json, Encoding.UTF8, "application/json");

    private static HttpContent AvatarContent()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 1, 2, 3 });
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "khu.png");
        return form;
    }

    private static IEnumerable<ApiCall> Cases()
    {
        var area = AreaId;
        var row = RowId;
        var box = BoxId;
        var alert = AlertId;
        var technical = TechnicalAlertId;
        var threshold = ThresholdId;
        var areaJson = "{\"name\":\"Khu A\"}";
        var rowJson = "{\"farmingAreaId\":\"" + area + "\",\"name\":\"Dãy 1\"}";
        var boxJson = "{\"farmingRowId\":\"" + row + "\",\"code\":\"B01\"}";
        var boxUpdate = "{\"code\":\"B01\",\"status\":\"empty\",\"isOccupied\":false}";
        var boxStatus = "{\"status\":\"empty\",\"isOccupied\":false}";
        var thresholdCreate = "{\"sensorType\":\"ph\",\"minValue\":6.5,\"maxValue\":8.5}";
        var thresholdUpdate = "{\"minValue\":6.5,\"maxValue\":8.5,\"severity\":\"Warning\",\"isActive\":true}";
        var setting = "{\"value\":\"CrabSense\"}";
        var technicalSetting = "{\"value\":\"secret\",\"category\":\"system\"}";

        // Cấu trúc khu / dãy / hộp: chỉ Farm Administrator.
        foreach (var call in new[]
                 {
                     new ApiCall(HttpMethod.Post, "/api/farming-areas", areaJson),
                     new ApiCall(HttpMethod.Post, "/api/farming-areas/avatar", null, multipart: true),
                     new ApiCall(HttpMethod.Post, $"/api/farming-areas/{area}/avatar", null, multipart: true),
                     new ApiCall(HttpMethod.Put, $"/api/farming-areas/{area}", areaJson),
                     new ApiCall(HttpMethod.Patch, $"/api/farming-areas/{area}/map", "{}"),
                     new ApiCall(HttpMethod.Delete, $"/api/farming-areas/{area}", null),
                     new ApiCall(HttpMethod.Post, $"/api/farming-areas/{area}/rows", rowJson),
                     new ApiCall(HttpMethod.Post, "/api/farming-rows", rowJson),
                     new ApiCall(HttpMethod.Put, $"/api/farming-rows/{row}", "{\"name\":\"Dãy 1\"}"),
                     new ApiCall(HttpMethod.Patch, $"/api/farming-rows/{row}/map", "{}"),
                     new ApiCall(HttpMethod.Delete, $"/api/farming-rows/{row}", null),
                     new ApiCall(HttpMethod.Post, $"/api/farming-rows/{row}/boxes", boxJson),
                     new ApiCall(HttpMethod.Post, $"/api/farming-rows/{row}/boxes/bulk", "{\"quantity\":1}"),
                     new ApiCall(HttpMethod.Post, "/api/boxes", boxJson),
                     new ApiCall(HttpMethod.Put, $"/api/boxes/{box}", boxUpdate),
                     new ApiCall(HttpMethod.Patch, $"/api/boxes/{box}/map", "{}"),
                     new ApiCall(HttpMethod.Delete, $"/api/boxes/{box}", null)
                 })
        {
            yield return call.OnlyAdmin();
        }

        yield return new ApiCall(HttpMethod.Patch, $"/api/boxes/{box}/status", boxStatus).StaffOnly();
        yield return new ApiCall(HttpMethod.Get, "/api/farming-rows", null).FarmPortal();
        yield return new ApiCall(HttpMethod.Get, "/api/boxes", null).FarmPortal();
        yield return new ApiCall(HttpMethod.Post, $"/api/boxes/{box}/crabs", "{}").StaffOnly();

        yield return new ApiCall(HttpMethod.Get, "/api/alert-thresholds", null).FarmPortal();
        yield return new ApiCall(HttpMethod.Post, "/api/alert-thresholds", thresholdCreate).OnlyAdmin();
        yield return new ApiCall(HttpMethod.Put, $"/api/alert-thresholds/{threshold}", thresholdUpdate).OnlyAdmin();
        yield return new ApiCall(HttpMethod.Delete, $"/api/alert-thresholds/{threshold}", null).OnlyAdmin();

        yield return new ApiCall(HttpMethod.Get, "/api/alerts", null).FarmPortal();
        yield return new ApiCall(HttpMethod.Get, "/api/alerts/history", null).FarmPortal();
        yield return new ApiCall(HttpMethod.Get, $"/api/alerts/{alert}", null).FarmOnly();
        yield return new ApiCall(HttpMethod.Post, $"/api/alerts/{alert}/acknowledge", "{}").FarmOnly();
        yield return new ApiCall(HttpMethod.Patch, $"/api/alerts/{alert}/acknowledge", "{}").FarmOnly();
        yield return new ApiCall(HttpMethod.Post, $"/api/alerts/{technical}/acknowledge", "{}").FarmOnly();
        yield return new ApiCall(HttpMethod.Post, $"/api/alerts/{alert}/process", "{}").ManagerAndStaff();
        yield return new ApiCall(HttpMethod.Patch, $"/api/alerts/{alert}/resolve", "{}").ManagerAndStaff();
        yield return new ApiCall(HttpMethod.Post, $"/api/alerts/{alert}/resolve", "{}").ManagerAndStaff();
        yield return new ApiCall(HttpMethod.Post, "/api/alerts/check-disconnects", null).SystemOnly();

        yield return new ApiCall(HttpMethod.Get, "/api/notification-rules", null).AdminAndSystem();
        yield return new ApiCall(HttpMethod.Put, "/api/notification-rules/sensor_threshold", "{}").OnlyAdmin();
        yield return new ApiCall(HttpMethod.Put, "/api/notification-rules/device_disconnect", "{}").SystemOnly();
        yield return new ApiCall(HttpMethod.Post, "/api/notification-rules/sensor_threshold/preview", "{}").OnlyAdmin();
        yield return new ApiCall(HttpMethod.Post, "/api/notification-rules/device_disconnect/preview", "{}").SystemOnly();

        yield return new ApiCall(HttpMethod.Get, "/api/dashboard/overview", null).FarmPortal();
        yield return new ApiCall(HttpMethod.Get, "/api/dashboard/metrics", null).FarmPortal();

        foreach (var path in new[]
                 {
                     "/api/reports/harvest",
                     "/api/reports/harvest/period",
                     "/api/reports/inventory",
                     "/api/reports/inventory/filtered",
                     "/api/reports/survival-rate",
                     "/api/reports/molting",
                     "/api/reports/molting/filtered",
                     "/api/reports/operational-efficiency",
                     "/api/reports/crab-lots/compare"
                 })
        {
            yield return new ApiCall(HttpMethod.Get, path, null).AdminAndManager();
        }

        yield return new ApiCall(HttpMethod.Put, "/api/settings", "{\"key\":\"site-name\",\"value\":\"CrabSense\"}").OnlyAdmin();
        yield return new ApiCall(HttpMethod.Get, "/api/settings", null).OnlyAdmin();
        yield return new ApiCall(HttpMethod.Get, "/api/settings/site-name", null).OnlyAdmin();
        yield return new ApiCall(HttpMethod.Put, "/api/settings/site-name", setting).OnlyAdmin();
        yield return new ApiCall(HttpMethod.Put, "/api/settings/firmware", technicalSetting).Nobody();
        yield return new ApiCall(HttpMethod.Delete, "/api/settings/site-name", null).OnlyAdmin();
    }

    private static IFarmingService StubFarming() => new Mock<IFarmingService>().Object;

    private static IAlertService StubAlerts()
    {
        var alerts = new Mock<IAlertService>();
        alerts.Setup(s => s.GetAlertsAsync(
                It.IsAny<bool?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IEnumerable<AlertDto>>.Ok(Array.Empty<AlertDto>()));
        alerts.Setup(s => s.GetHistoryAsync(It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IEnumerable<AlertDto>>.Ok(Array.Empty<AlertDto>()));
        alerts.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                ApiResponse<AlertDto>.Ok(id == TechnicalAlertId ? SampleAlert("Device") : SampleAlert("Environment")));
        alerts.Setup(s => s.AcknowledgeAsync(It.IsAny<Guid>(), It.IsAny<AcknowledgeAlertRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<AlertDto>.Ok(SampleAlert("Device")));
        alerts.Setup(s => s.StartProcessingAsync(It.IsAny<Guid>(), It.IsAny<StartProcessingRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<AlertDto>.Ok(SampleAlert("Environment")));
        alerts.Setup(s => s.ResolveAsync(It.IsAny<Guid>(), It.IsAny<ResolveAlertRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<AlertDto>.Ok(SampleAlert("Environment")));
        alerts.Setup(s => s.CheckDisconnectsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<int>.Ok(0));
        return alerts.Object;
    }

    private static INotificationService StubNotifications()
    {
        var rules = new Mock<INotificationService>();
        rules.Setup(s => s.GetRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IEnumerable<NotificationRuleDto>>.Ok(Array.Empty<NotificationRuleDto>()));
        return rules.Object;
    }

    private static IDashboardService StubDashboard() => new Mock<IDashboardService>().Object;

    private static IUnitOfWork StubUnitOfWork()
    {
        var settings = new List<FarmSettings>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Key = "site-name",
                Value = "CrabSense",
                Category = "general"
            }
        };
        var repo = new Mock<IRepository<FarmSettings>>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        repo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmSettings, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<FarmSettings, bool>> predicate, CancellationToken _) =>
                settings.FirstOrDefault(predicate.Compile()));
        repo.Setup(r => r.AddAsync(It.IsAny<FarmSettings>(), It.IsAny<CancellationToken>()))
            .Callback<FarmSettings, CancellationToken>((item, _) => settings.Add(item))
            .Returns(Task.CompletedTask);

        var uow = new Mock<IUnitOfWork>();
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new AppUser
            {
                Id = id,
                Role = UserRole.FarmAdministrator,
                IsActive = true,
                Username = "scoped"
            });
        var areas = new Mock<IRepository<FarmingArea>>();
        areas.Setup(r => r.Query()).Returns(() => Array.Empty<FarmingArea>().AsQueryable());

        uow.Setup(u => u.FarmSettings).Returns(repo.Object);
        uow.Setup(u => u.Users).Returns(users.Object);
        uow.Setup(u => u.FarmingAreas).Returns(areas.Object);
        uow.Setup(u => u.FarmingRows).Returns(EmptyRepo<FarmingRow>());
        uow.Setup(u => u.Boxes).Returns(EmptyRepo<Box>());
        uow.Setup(u => u.CrabLots).Returns(EmptyRepo<CrabLot>());
        uow.Setup(u => u.Crabs).Returns(EmptyRepo<Crab>());
        uow.Setup(u => u.HarvestLines).Returns(EmptyRepo<HarvestLine>());
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return uow.Object;
    }

    private static IRepository<T> EmptyRepo<T>() where T : class
    {
        var repo = new Mock<IRepository<T>>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<T>());
        return repo.Object;
    }

    private static AlertDto SampleAlert(string category) => new(
        Guid.NewGuid(), null, "Cảnh báo", "Warning", "Open", null, DateTime.UtcNow, null,
        "Cảnh báo", category, null, null, null, null, null, null, null, null,
        1, "test", "ok", null, null, null);

    private sealed record ApiCall(
        HttpMethod Method,
        string Path,
        string? Json,
        int Admin = 403,
        int Manager = 403,
        int Staff = 403,
        int System = 403,
        int Anonymous = 401,
        bool Multipart = false)
    {
        public ApiCall(HttpMethod method, string path, string? json, bool multipart)
            : this(method, path, json, Multipart: multipart)
        {
        }

        public ApiCall OnlyAdmin() => this with { Admin = 200 };
        public ApiCall StaffOnly() => this with { Staff = 200 };
        public ApiCall FarmPortal() => this with { Admin = 200, Manager = 200, Staff = 200 };
        public ApiCall FarmWrite() => this with { Admin = 200, Manager = 200, Staff = 200 };
        public ApiCall FarmOnly() => this with { Admin = 200, Manager = 200, Staff = 200 };
        public ApiCall AnyAuthenticated() => this with { Admin = 200, Manager = 200, Staff = 200, System = 200 };
        public ApiCall ManagerAndStaff() => this with { Manager = 200, Staff = 200 };
        public ApiCall SystemOnly() => this with { System = 200 };
        public ApiCall AdminAndSystem() => this with { Admin = 200, System = 200 };
        public ApiCall AdminAndManager() => this with { Admin = 200, Manager = 200 };
        public ApiCall Nobody() => this;
    }
}

public sealed class CreateFarmingRowPermissionTests : IAsyncLifetime
{
    private static readonly Guid AreaId = Guid.Parse("abababab-abab-abab-abab-abababababab");
    private static readonly Guid AdminId = Guid.Parse("10101010-1010-1010-1010-101010101010");
    private static readonly Guid OtherAdminId = Guid.Parse("20202020-2020-2020-2020-202020202020");
    private static readonly Guid ManagerId = Guid.Parse("30303030-3030-3030-3030-303030303030");
    private static readonly Guid StaffId = Guid.Parse("40404040-4040-4040-4040-404040404040");
    private static readonly Guid SystemId = Guid.Parse("50505050-5050-5050-5050-505050505050");

    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        var area = new FarmingArea
        {
            Id = AreaId,
            Code = "AREA-A01",
            Name = "Khu A",
            IsActive = true,
            Status = FarmStatus.Active,
            Administrators =
            {
                new AppUser
                {
                    Id = AdminId,
                    Role = UserRole.FarmAdministrator,
                    IsActive = true,
                    Username = "admin-a"
                }
            }
        };
        var users = new Dictionary<Guid, AppUser>
        {
            [AdminId] = area.Administrators.Single(),
            [OtherAdminId] = new AppUser
            {
                Id = OtherAdminId,
                Role = UserRole.FarmAdministrator,
                IsActive = true,
                Username = "admin-b"
            },
            [ManagerId] = new AppUser { Id = ManagerId, Role = UserRole.FarmManager, IsActive = true, Username = "manager" },
            [StaffId] = new AppUser { Id = StaffId, Role = UserRole.FarmStaff, IsActive = true, Username = "staff" },
            [SystemId] = new AppUser { Id = SystemId, Role = UserRole.SystemAdministrator, IsActive = true, Username = "sys" }
        };

        var areas = new Mock<IRepository<FarmingArea>>();
        areas.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == AreaId ? area : null);
        areas.Setup(r => r.Query()).Returns(() => new[] { area }.AsQueryable());

        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmingRow, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FarmingRow>());
        rows.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<FarmingRow>());
        rows.Setup(r => r.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmingRow, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        rows.Setup(r => r.AddAsync(It.IsAny<FarmingRow>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => users.GetValueOrDefault(id));

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.FarmingAreas).Returns(areas.Object);
        uow.Setup(u => u.FarmingRows).Returns(rows.Object);
        uow.Setup(u => u.Users).Returns(userRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(uow.Object);
            services.AddSingleton(new Mock<IBoxQrService>().Object);
            services.AddSingleton(new Mock<IPublicImageStorage>().Object);
            services.AddScoped<IFarmingService, FarmingService>();
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Post_farming_rows_matches_the_role_and_area_matrix()
    {
        var body = JsonSerializer.Serialize(new { farmingAreaId = AreaId, name = "Dãy 1" });
        var cases = new (string? Role, Guid? UserId, int Expected)[]
        {
            (AppRoles.FarmAdministrator, AdminId, 200),
            (AppRoles.FarmAdministrator, OtherAdminId, 403),
            (AppRoles.FarmManager, ManagerId, 403),
            (AppRoles.FarmStaff, StaffId, 403),
            (AppRoles.SystemAdministrator, SystemId, 403),
            (null, null, 401)
        };

        var failures = new List<string>();
        foreach (var (role, userId, expected) in cases)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _api.SendAsync(role, userId, HttpMethod.Post, "/api/farming-rows", content);
            var actual = (int)response.StatusCode;
            var allowed = expected == 200 ? actual is 200 or 201 : actual == expected;
            if (allowed)
                continue;
            failures.Add(
                $"{role ?? "Chưa đăng nhập"}: mong đợi {expected}, nhận {actual}. {await response.Content.ReadAsStringAsync()}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}

/// <summary>Bước 1 — cấu trúc khu/dãy/hộp, gồm đúng khu và API nghiệp vụ giữ nguyên.</summary>
public sealed class FarmStructurePermissionTests : IAsyncLifetime
{
    private static readonly Guid AreaId = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
    private static readonly Guid RowId = Guid.Parse("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2");
    private static readonly Guid BoxId = Guid.Parse("c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3");
    private static readonly Guid AdminId = Guid.Parse("d4d4d4d4-d4d4-d4d4-d4d4-d4d4d4d4d4d4");
    private static readonly Guid OtherAdminId = Guid.Parse("e5e5e5e5-e5e5-e5e5-e5e5-e5e5e5e5e5e5");
    private static readonly Guid ManagerId = Guid.Parse("f6f6f6f6-f6f6-f6f6-f6f6-f6f6f6f6f6f6");
    private static readonly Guid StaffId = Guid.Parse("17171717-1717-1717-1717-171717171717");
    private static readonly Guid SystemId = Guid.Parse("18181818-1818-1818-1818-181818181818");

    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        var admin = new AppUser
        {
            Id = AdminId,
            Role = UserRole.FarmAdministrator,
            IsActive = true,
            Username = "fa-a"
        };
        var area = new FarmingArea
        {
            Id = AreaId,
            Code = "AREA-A01",
            Name = "Khu A",
            IsActive = true,
            Status = FarmStatus.Active,
            Administrators = { admin }
        };
        var row = new FarmingRow
        {
            Id = RowId,
            FarmingAreaId = AreaId,
            Code = "DAY-A01",
            Name = "Dãy 1",
            IsActive = true,
            Status = FarmStatus.Active
        };
        var box = new Box { Id = BoxId, FarmingRowId = RowId, Code = "BOX-0001", Status = "empty" };
        var users = new Dictionary<Guid, AppUser>
        {
            [AdminId] = admin,
            [OtherAdminId] = new AppUser { Id = OtherAdminId, Role = UserRole.FarmAdministrator, IsActive = true, Username = "fa-b" },
            [ManagerId] = new AppUser { Id = ManagerId, Role = UserRole.FarmManager, IsActive = true, Username = "fm" },
            [StaffId] = new AppUser { Id = StaffId, Role = UserRole.FarmStaff, IsActive = true, Username = "fs" },
            [SystemId] = new AppUser { Id = SystemId, Role = UserRole.SystemAdministrator, IsActive = true, Username = "sa" }
        };

        var areas = new Mock<IRepository<FarmingArea>>();
        areas.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == AreaId ? area : null);
        areas.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { area });
        areas.Setup(r => r.Query()).Returns(() => new[] { area }.AsQueryable());

        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == RowId ? row : null);
        rows.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { row });
        rows.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmingRow, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FarmingRow>());
        rows.Setup(r => r.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmingRow, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == BoxId ? box : null);
        boxes.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { box });
        boxes.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Box, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Box>());
        boxes.Setup(r => r.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Box, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        boxes.Setup(r => r.AddAsync(It.IsAny<Box>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => users.GetValueOrDefault(id));

        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Crab>());
        var alerts = new Mock<IRepository<Alert>>();
        alerts.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Alert>());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.FarmingAreas).Returns(areas.Object);
        uow.Setup(u => u.FarmingRows).Returns(rows.Object);
        uow.Setup(u => u.Boxes).Returns(boxes.Object);
        uow.Setup(u => u.Users).Returns(userRepo.Object);
        uow.Setup(u => u.Crabs).Returns(crabs.Object);
        uow.Setup(u => u.Alerts).Returns(alerts.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var detail = new Mock<IBoxDetailService>();
        detail.Setup(s => s.AddCrabAsync(
                It.IsAny<Guid>(),
                It.IsAny<CrabSenseBE.Application.DTOs.Ops.MobileAddCrabRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<CrabSenseBE.Application.DTOs.Farm.BoxCrabItemDto>.Ok(null!));

        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(uow.Object);
            services.AddSingleton(new Mock<IBoxQrService>().Object);
            services.AddSingleton(new Mock<IPublicImageStorage>().Object);
            services.AddSingleton(new Mock<IBoxOverviewService>().Object);
            services.AddSingleton(detail.Object);
            services.AddScoped<IFarmingService, FarmingService>();
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Structure_endpoints_match_step1_matrix()
    {
        var cases = new (string Name, string? Role, Guid? UserId, HttpMethod Method, string Path, string? Json, int Expected)[]
        {
            ("FA tạo hộp trong khu mình quản lý", AppRoles.FarmAdministrator, AdminId, HttpMethod.Post, "/api/boxes",
                "{\"farmingRowId\":\"" + RowId + "\",\"code\":\"BOX-0099\"}", 200),
            ("FA sửa hộp trong khu khác", AppRoles.FarmAdministrator, OtherAdminId, HttpMethod.Put, $"/api/boxes/{BoxId}",
                "{\"code\":\"BOX-0001\",\"status\":\"empty\",\"isOccupied\":false}", 403),
            ("FM tạo dãy", AppRoles.FarmManager, ManagerId, HttpMethod.Post, "/api/farming-rows",
                "{\"farmingAreaId\":\"" + AreaId + "\",\"name\":\"Dãy mới\"}", 403),
            ("FS sửa hộp", AppRoles.FarmStaff, StaffId, HttpMethod.Put, $"/api/boxes/{BoxId}",
                "{\"code\":\"BOX-0001\",\"status\":\"empty\",\"isOccupied\":false}", 403),
            ("SA xóa khu", AppRoles.SystemAdministrator, SystemId, HttpMethod.Delete, $"/api/farming-areas/{AreaId}",
                null, 403),
            ("FS gán cua vào hộp", AppRoles.FarmStaff, StaffId, HttpMethod.Post, $"/api/boxes/{BoxId}/crabs",
                "{}", 200),
            ("Không đăng nhập", null, null, HttpMethod.Post, "/api/boxes",
                "{\"farmingRowId\":\"" + RowId + "\"}", 401)
        };

        var failures = new List<string>();
        foreach (var item in cases)
        {
            using var content = item.Json is null
                ? null
                : new StringContent(item.Json, Encoding.UTF8, "application/json");
            using var response = await _api.SendAsync(item.Role, item.UserId, item.Method, item.Path, content);
            var actual = (int)response.StatusCode;
            var ok = item.Expected == 200 ? actual is 200 or 201 : actual == item.Expected;
            if (ok)
                continue;
            failures.Add($"{item.Name}: mong đợi {item.Expected}, nhận {actual}. {await response.Content.ReadAsStringAsync()}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}

/// <summary>Bước 2 — hai khu A/B, id trên URL không vượt quyền.</summary>
public sealed class FarmAreaScopePermissionTests : IAsyncLifetime
{
    private static readonly Guid AreaA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid AreaB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid RowA = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid RowB = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b1");
    private static readonly Guid BoxA = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a2");
    private static readonly Guid BoxB = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b2");
    private static readonly Guid AdminA = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000f1");
    private static readonly Guid AdminB = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000f2");
    private static readonly Guid ManagerA = Guid.Parse("cccccccc-0000-0000-0000-0000000000f3");
    private static readonly Guid StaffA = Guid.Parse("dddddddd-0000-0000-0000-0000000000f4");

    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        var adminA = new AppUser { Id = AdminA, Role = UserRole.FarmAdministrator, IsActive = true, Username = "fa-a" };
        var adminB = new AppUser { Id = AdminB, Role = UserRole.FarmAdministrator, IsActive = true, Username = "fa-b" };
        var manager = new AppUser { Id = ManagerA, Role = UserRole.FarmManager, IsActive = true, Username = "fm-a" };
        var staff = new AppUser { Id = StaffA, Role = UserRole.FarmStaff, IsActive = true, Username = "fs-a" };
        var areaA = new FarmingArea
        {
            Id = AreaA, Code = "AREA-A01", Name = "Khu A", IsActive = true, Status = FarmStatus.Active,
            Administrators = { adminA }
        };
        var areaB = new FarmingArea
        {
            Id = AreaB, Code = "AREA-B01", Name = "Khu B", IsActive = true, Status = FarmStatus.Active,
            Administrators = { adminB }
        };
        var rowA = new FarmingRow { Id = RowA, FarmingAreaId = AreaA, Code = "DAY-A01", Name = "Dãy A", IsActive = true };
        var rowB = new FarmingRow { Id = RowB, FarmingAreaId = AreaB, Code = "DAY-B01", Name = "Dãy B", IsActive = true };
        var boxA = new Box { Id = BoxA, FarmingRowId = RowA, Code = "BOX-A", Status = "empty" };
        var boxB = new Box { Id = BoxB, FarmingRowId = RowB, Code = "BOX-B", Status = "empty" };
        var areaList = new[] { areaA, areaB };
        var rowList = new[] { rowA, rowB };
        var boxList = new[] { boxA, boxB };
        var users = new Dictionary<Guid, AppUser>
        {
            [AdminA] = adminA, [AdminB] = adminB, [ManagerA] = manager, [StaffA] = staff
        };

        var areaRepo = new Mock<IRepository<FarmingArea>>();
        areaRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => areaList.FirstOrDefault(area => area.Id == id));
        areaRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(areaList);
        areaRepo.Setup(r => r.Query()).Returns(() => areaList.AsQueryable());

        var rowRepo = new Mock<IRepository<FarmingRow>>();
        rowRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => rowList.FirstOrDefault(row => row.Id == id));
        rowRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rowList);

        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => boxList.FirstOrDefault(box => box.Id == id));
        boxRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(boxList);

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

        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Crab>());
        var alerts = new Mock<IRepository<Alert>>();
        alerts.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Alert, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Alert>());
        alerts.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Alert>());
        var water = new Mock<IRepository<WaterSystem>>();
        water.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<WaterSystem>());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        uow.Setup(u => u.FarmingRows).Returns(rowRepo.Object);
        uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        uow.Setup(u => u.Users).Returns(userRepo.Object);
        uow.Setup(u => u.FarmManagerAssignments).Returns(managers.Object);
        uow.Setup(u => u.FarmStaffAssignments).Returns(staffRepo.Object);
        uow.Setup(u => u.Crabs).Returns(crabs.Object);
        uow.Setup(u => u.Alerts).Returns(alerts.Object);
        uow.Setup(u => u.WaterSystems).Returns(water.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(uow.Object);
            services.AddSingleton(new Mock<IBoxQrService>().Object);
            services.AddSingleton(new Mock<IPublicImageStorage>().Object);
            services.AddSingleton(new Mock<IBoxOverviewService>().Object);
            services.AddScoped<IFarmingService, FarmingService>();
            services.AddScoped<IBoxDetailService, BoxDetailService>();
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Area_row_and_box_ids_cannot_cross_the_assigned_area()
    {
        var rename = "{\"name\":\"Khu A đã sửa\"}";
        var status = "{\"status\":\"active\",\"isOccupied\":true}";
        var boxEdit = "{\"code\":\"BOX-B\",\"status\":\"empty\",\"isOccupied\":false}";
        var cases = new (string Name, string Role, Guid UserId, HttpMethod Method, string Path, string? Json, int Expected)[]
        {
            ("FA khu A sửa khu A", AppRoles.FarmAdministrator, AdminA, HttpMethod.Put, $"/api/farming-areas/{AreaA}", rename, 200),
            ("FA khu A sửa khu B", AppRoles.FarmAdministrator, AdminA, HttpMethod.Put, $"/api/farming-areas/{AreaB}", rename, 403),
            ("FM khu A xem khu B", AppRoles.FarmManager, ManagerA, HttpMethod.Get, $"/api/farming-areas/{AreaB}", null, 403),
            ("FS khu A thao tác hộp khu B", AppRoles.FarmStaff, StaffA, HttpMethod.Patch, $"/api/boxes/{BoxB}/status", status, 403),
            ("FA khu A xem dãy khu B", AppRoles.FarmAdministrator, AdminA, HttpMethod.Get, $"/api/farming-rows/{RowB}", null, 403),
            ("FM khu A xem hộp khu B", AppRoles.FarmManager, ManagerA, HttpMethod.Get, $"/api/boxes/{BoxB}", null, 403),
            ("FA khu A sửa hộp khu B", AppRoles.FarmAdministrator, AdminA, HttpMethod.Put, $"/api/boxes/{BoxB}", boxEdit, 403),
            ("FM khu A lọc dãy theo khu B", AppRoles.FarmManager, ManagerA, HttpMethod.Get, $"/api/farming-rows?farmingAreaId={AreaB}", null, 403),
            ("FS khu A gán cua vào hộp khu B", AppRoles.FarmStaff, StaffA, HttpMethod.Post, $"/api/boxes/{BoxB}/crabs", "{}", 403)
        };

        var failures = new List<string>();
        foreach (var item in cases)
        {
            using var content = item.Json is null ? null : new StringContent(item.Json, Encoding.UTF8, "application/json");
            using var response = await _api.SendAsync(item.Role, item.UserId, item.Method, item.Path, item.Path.Contains("crabs") || item.Json is not null ? content : null);
            var actual = (int)response.StatusCode;
            var ok = item.Expected == 200 ? actual is 200 or 201 : actual == item.Expected;
            if (!ok)
                failures.Add($"{item.Name}: mong đợi {item.Expected}, nhận {actual}. {await response.Content.ReadAsStringAsync()}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
