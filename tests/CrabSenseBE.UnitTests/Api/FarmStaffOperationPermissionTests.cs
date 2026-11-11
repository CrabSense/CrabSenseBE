using System.Text;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.DTOs.Mortality;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

public sealed class FarmStaffOperationPermissionTests : IAsyncLifetime
{
    private static readonly Guid CrabId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid LotId = Guid.Parse("11111111-0000-0000-0000-000000000002");
    private static readonly Guid MoltingId = Guid.Parse("11111111-0000-0000-0000-000000000003");
    private static readonly Guid BoxId = Guid.Parse("11111111-0000-0000-0000-000000000004");
    private static readonly Guid AdminId = Guid.Parse("11111111-0000-0000-0000-000000000005");
    private static readonly Guid ManagerId = Guid.Parse("11111111-0000-0000-0000-000000000006");
    private static readonly Guid StaffId = Guid.Parse("11111111-0000-0000-0000-000000000007");
    private static readonly Guid SystemId = Guid.Parse("11111111-0000-0000-0000-000000000008");

    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        var farming = new Mock<IFarmingService>();
        farming.Setup(service => service.CreateCrabAsync(
                It.IsAny<CreateCrabRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<CrabDto>.Ok(null!));
        farming.Setup(service => service.UpdateCrabAsync(
                CrabId, It.IsAny<UpdateCrabRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<CrabDto>.Ok(null!));
        farming.Setup(service => service.DeleteCrabAsync(CrabId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse.Ok());

        var history = new Mock<IFarmHistoryService>();
        history.Setup(service => service.CreateMoltingAsync(
                It.IsAny<CreateMoltingRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<MoltingRecordDto>.Ok(null!));
        history.Setup(service => service.UpdateMoltingAsync(
                MoltingId, It.IsAny<UpdateMoltingRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<MoltingRecordDto>.Ok(null!));
        history.Setup(service => service.VoidMoltingAsync(
                MoltingId, It.IsAny<VoidCrabEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<MoltingRecordDto>.Ok(null!));

        var mortality = new Mock<IMortalityService>();
        mortality.Setup(service => service.RecordMortalityAsync(
                StaffId,
                It.IsAny<RecordMortalityRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MortalityRecordDto)null!);

        var inspections = new Mock<IManualInspectionService>();
        inspections.Setup(service => service.SubmitAsync(
                It.IsAny<SubmitManualInspectionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ManualInspectionDto>.Ok(null!));

        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(farming.Object);
            services.AddSingleton(history.Object);
            services.AddSingleton(mortality.Object);
            services.AddSingleton(inspections.Object);
            services.AddSingleton(new Mock<ICrabImageService>().Object);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Staff_records_operations_but_cannot_delete_or_rewrite_confirmed_history()
    {
        var failures = new List<string>();
        var createCrab = $"{{\"crabLotId\":\"{LotId}\"}}";
        var updateCrab = "{\"moltingStage\":\"premolt\"}";
        var createMolt = "{}";
        var correctMolt = "{\"correctionReason\":\"Nhập sai\"}";
        var voidMolt = "{\"reason\":\"Bản ghi trùng\"}";
        var mortality = $"{{\"crabId\":\"{CrabId}\",\"cause\":0}}";
        var inspection = $"{{\"boxId\":\"{BoxId}\"}}";

        await Expect(failures, "FS tạo cua", 200, AppRoles.FarmStaff, StaffId,
            HttpMethod.Post, "/api/crabs", createCrab);
        await Expect(failures, "FM tạo cua", 403, AppRoles.FarmManager, ManagerId,
            HttpMethod.Post, "/api/crabs", createCrab);
        await Expect(failures, "FA tạo cua", 403, AppRoles.FarmAdministrator, AdminId,
            HttpMethod.Post, "/api/crabs", createCrab);
        await Expect(failures, "FS sửa thông tin thực tế", 200, AppRoles.FarmStaff, StaffId,
            HttpMethod.Put, $"/api/crabs/{CrabId}", updateCrab);
        await Expect(failures, "FS xóa cua", 403, AppRoles.FarmStaff, StaffId,
            HttpMethod.Delete, $"/api/crabs/{CrabId}", null);
        await Expect(failures, "FM xử lý bản ghi cua cuối vòng đời", 200, AppRoles.FarmManager, ManagerId,
            HttpMethod.Delete, $"/api/crabs/{CrabId}", null);

        await Expect(failures, "FS ghi nhận lột", 200, AppRoles.FarmStaff, StaffId,
            HttpMethod.Post, $"/api/crabs/{CrabId}/moltings", createMolt);
        await Expect(failures, "FS sửa lịch sử lột", 403, AppRoles.FarmStaff, StaffId,
            HttpMethod.Put, $"/api/moltings/{MoltingId}", correctMolt);
        await Expect(failures, "FS vô hiệu hóa lịch sử lột", 403, AppRoles.FarmStaff, StaffId,
            HttpMethod.Post, $"/api/moltings/{MoltingId}/void", voidMolt);
        await Expect(failures, "FM sửa lịch sử lột có lý do", 200, AppRoles.FarmManager, ManagerId,
            HttpMethod.Put, $"/api/moltings/{MoltingId}", correctMolt);

        await Expect(failures, "FS ghi nhận tử vong", 200, AppRoles.FarmStaff, StaffId,
            HttpMethod.Post, "/api/mortality", mortality);
        await Expect(failures, "SA ghi nhận tử vong", 403, AppRoles.SystemAdministrator, SystemId,
            HttpMethod.Post, "/api/mortality", mortality);
        await Expect(failures, "FS ghi inspection", 200, AppRoles.FarmStaff, StaffId,
            HttpMethod.Post, "/api/inspections", inspection);
        await Expect(failures, "FM ghi inspection thay Staff", 403, AppRoles.FarmManager, ManagerId,
            HttpMethod.Post, "/api/inspections", inspection);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private async Task Expect(
        List<string> failures,
        string name,
        int expected,
        string role,
        Guid userId,
        HttpMethod method,
        string path,
        string? json)
    {
        using var content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _api.SendAsync(role, userId, method, path, content);
        var actual = (int)response.StatusCode;
        var success = expected == 200 ? actual is 200 or 201 or 204 : actual == expected;
        if (!success)
            failures.Add(
                $"{name}: mong đợi {expected}, nhận {actual}. " +
                await response.Content.ReadAsStringAsync());
    }
}
