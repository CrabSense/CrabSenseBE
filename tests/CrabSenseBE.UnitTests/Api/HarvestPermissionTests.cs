using System.Text;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

public sealed class HarvestPermissionTests : IAsyncLifetime
{
    private static readonly Guid VoucherId =
        Guid.Parse("88888888-0000-0000-0000-000000000001");
    private static readonly Guid ManagerId =
        Guid.Parse("88888888-0000-0000-0000-000000000002");
    private static readonly Guid StaffId =
        Guid.Parse("88888888-0000-0000-0000-000000000003");
    private static readonly Guid SystemId =
        Guid.Parse("88888888-0000-0000-0000-000000000004");
    private static readonly Guid AdminId =
        Guid.Parse("88888888-0000-0000-0000-000000000005");

    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(new Mock<IHarvestService>().Object);
            services.AddSingleton(new Mock<ICrabImageService>().Object);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Staff_cannot_approve_manager_cannot_execute_and_system_admin_is_denied()
    {
        var failures = new List<string>();
        await Expect(
            failures, "FS tự duyệt phiếu", 403,
            AppRoles.FarmStaff, StaffId,
            $"/api/harvest-vouchers/{VoucherId}/approve", "{}");
        await Expect(
            failures, "FA duyệt phiếu", 403,
            AppRoles.FarmAdministrator, AdminId,
            $"/api/harvest-vouchers/{VoucherId}/approve", "{}");
        await Expect(
            failures, "FM ghi hoàn tất thu hoạch thay Staff", 403,
            AppRoles.FarmManager, ManagerId,
            $"/api/harvest-vouchers/{VoucherId}/execute", "{\"lines\":[]}");
        await Expect(
            failures, "SA duyệt phiếu", 403,
            AppRoles.SystemAdministrator, SystemId,
            $"/api/harvest-vouchers/{VoucherId}/approve", "{}");
        await Expect(
            failures, "SA thực hiện thu hoạch", 403,
            AppRoles.SystemAdministrator, SystemId,
            $"/api/harvest-vouchers/{VoucherId}/execute", "{\"lines\":[]}");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private async Task Expect(
        List<string> failures,
        string name,
        int expected,
        string role,
        Guid userId,
        string path,
        string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _api.SendAsync(role, userId, HttpMethod.Post, path, content);
        if ((int)response.StatusCode != expected)
            failures.Add(
                $"{name}: mong đợi {expected}, nhận {(int)response.StatusCode}. " +
                await response.Content.ReadAsStringAsync());
    }
}
