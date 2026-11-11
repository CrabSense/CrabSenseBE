using System.Net.Http.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

public sealed class QrApprovalPermissionTests : IAsyncLifetime
{
    private static readonly Guid BoxId = Guid.Parse("10101010-0000-0000-0000-000000000001");
    private static readonly Guid LotId = Guid.Parse("10101010-0000-0000-0000-000000000002");
    private static readonly Guid QrId = Guid.Parse("10101010-0000-0000-0000-000000000003");
    private static readonly Guid AdminId = Guid.Parse("10101010-0000-0000-0000-000000000004");
    private static readonly Guid ManagerId = Guid.Parse("10101010-0000-0000-0000-000000000005");
    private static readonly Guid StaffId = Guid.Parse("10101010-0000-0000-0000-000000000006");
    private static readonly Guid SystemId = Guid.Parse("10101010-0000-0000-0000-000000000007");

    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        var boxQr = new Mock<IBoxQrService>();
        boxQr.Setup(service => service.EnsureBoxQrAsync(BoxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BoxQrDto>.Ok(
                new BoxQrDto(QrId, "BOX-1", BoxId, null, 0, true)));
        boxQr.Setup(service => service.ScanAsync("BOX-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BoxScanResultDto>.Ok(new BoxScanResultDto(
                new BoxQrDto(QrId, "BOX-1", BoxId, null, 0, true),
                new BoxScanBoxDto(BoxId, "BOX-1", "active", true, Guid.NewGuid()),
                [],
                "Khu A",
                "Dãy A")));

        var traceability = new Mock<ITraceabilityService>();
        traceability.Setup(service => service.EnsureFrozenLotQrAsync(
                LotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<QrCodeDto>.Ok(
                new QrCodeDto(QrId, "FL-1", LotId, 0, null, "Pending")));
        traceability.Setup(service => service.ApproveQrAsync(
                QrId, It.IsAny<ApproveQrAssignmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<QrCodeDto>.Ok(
                new QrCodeDto(QrId, "FL-1", LotId, 0, null, "Approved")));
        traceability.Setup(service => service.GetPendingReviewAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IReadOnlyList<QrCodeDto>>.Ok(
                new[] { new QrCodeDto(QrId, "FL-1", LotId, 0, null, "Pending") }));

        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(boxQr.Object);
            services.AddSingleton(traceability.Object);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Box_identity_and_product_assignment_have_separate_roles()
    {
        var failures = new List<string>();
        await Expect(failures, "FA tạo QR hộp", 200, AppRoles.FarmAdministrator, AdminId,
            HttpMethod.Post, $"/api/boxes/{BoxId}/qr");
        await Expect(failures, "FM tạo QR hộp", 403, AppRoles.FarmManager, ManagerId,
            HttpMethod.Post, $"/api/boxes/{BoxId}/qr");
        await Expect(failures, "FS tạo QR hộp", 403, AppRoles.FarmStaff, StaffId,
            HttpMethod.Post, $"/api/boxes/{BoxId}/qr");

        await Expect(failures, "FS quét QR hộp", 200, AppRoles.FarmStaff, StaffId,
            HttpMethod.Get, "/api/box-qr/scan?code=BOX-1");
        await Expect(failures, "FA quét QR hộp", 403, AppRoles.FarmAdministrator, AdminId,
            HttpMethod.Get, "/api/box-qr/scan?code=BOX-1");

        await Expect(failures, "FS đề xuất QR sản phẩm", 200, AppRoles.FarmStaff, StaffId,
            HttpMethod.Post, $"/api/frozen-lots/{LotId}/qr");
        await Expect(failures, "FM tự đề xuất QR sản phẩm", 403, AppRoles.FarmManager, ManagerId,
            HttpMethod.Post, $"/api/frozen-lots/{LotId}/qr");
        await Expect(failures, "FM duyệt QR sản phẩm", 200, AppRoles.FarmManager, ManagerId,
            HttpMethod.Post, $"/api/traceability-qrs/{QrId}/approve");
        await Expect(failures, "FS tự duyệt QR sản phẩm", 403, AppRoles.FarmStaff, StaffId,
            HttpMethod.Post, $"/api/traceability-qrs/{QrId}/approve");
        await Expect(failures, "SA duyệt QR sản phẩm", 403, AppRoles.SystemAdministrator, SystemId,
            HttpMethod.Post, $"/api/traceability-qrs/{QrId}/approve");
        await Expect(failures, "FM xem QR chờ rà soát", 200, AppRoles.FarmManager, ManagerId,
            HttpMethod.Get, "/api/traceability-qrs/pending-review");
        await Expect(failures, "FS xem QR chờ rà soát", 403, AppRoles.FarmStaff, StaffId,
            HttpMethod.Get, "/api/traceability-qrs/pending-review");
        await Expect(failures, "SA xem QR nội bộ", 403, AppRoles.SystemAdministrator, SystemId,
            HttpMethod.Get, $"/api/frozen-lots/{LotId}/qr");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private async Task Expect(
        List<string> failures,
        string name,
        int expected,
        string role,
        Guid userId,
        HttpMethod method,
        string path)
    {
        using var content = path.EndsWith("/approve", StringComparison.Ordinal)
            ? JsonContent.Create(new ApproveQrAssignmentRequest("Đã đối chiếu hồ sơ"))
            : null;
        using var response = await _api.SendAsync(role, userId, method, path, content);
        var actual = (int)response.StatusCode;
        var success = expected == 200 ? actual is 200 or 201 or 204 : actual == expected;
        if (!success)
            failures.Add(
                $"{name}: mong đợi {expected}, nhận {actual}. " +
                await response.Content.ReadAsStringAsync());
    }
}
