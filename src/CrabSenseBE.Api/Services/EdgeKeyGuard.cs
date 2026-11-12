using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Services;

/// <summary>
/// Xác thực request từ edge (kiosk/ESP32) bằng header X-Edge-Key.
/// Thiếu cấu hình EdgeSync:ApiKey thì ĐÓNG endpoint (fail-closed) — không được cho qua,
/// vì deploy thiếu config sẽ biến endpoint thành public.
/// </summary>
public static class EdgeKeyGuard
{
    public sealed record AuthenticatedEdgeDevice(
        Guid DeviceId,
        string DeviceCode,
        string LeaseHolder,
        bool IsLegacyCredential);

    public const string HeaderName = "X-Edge-Key";
    public const string DeviceHeaderName = "X-Device-Key";
    public const string ConfigKey = "EdgeSync:ApiKey";

    /// <returns>null nếu request hợp lệ; ngược lại là response cần trả về.</returns>
    public static IActionResult? Check(HttpRequest request, IConfiguration configuration)
    {
        var expected = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(expected))
        {
            return new ObjectResult(new
            {
                success = false,
                message = $"{ConfigKey} chưa được cấu hình — endpoint edge đang đóng."
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }

        var supplied = request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(supplied) || !FixedTimeEquals(supplied.Trim(), expected))
            return new UnauthorizedObjectResult(new { success = false, message = "X-Edge-Key không hợp lệ." });

        return null;
    }

    public static async Task<(IActionResult? Denied, AuthenticatedEdgeDevice? Identity)> CheckDeviceAsync(
        HttpRequest request,
        IConfiguration configuration,
        IUnitOfWork uow,
        string deviceCode,
        CancellationToken ct)
    {
        var code = deviceCode?.Trim();
        var device = string.IsNullOrWhiteSpace(code)
            ? null
            : await uow.Devices.FirstOrDefaultAsync(
                d => d.DeviceCode == code, ct);
        return AuthenticateDevice(request, configuration, device);
    }

    public static async Task<(IActionResult? Denied, AuthenticatedEdgeDevice? Identity)> CheckDeviceAsync(
        HttpRequest request,
        IConfiguration configuration,
        IUnitOfWork uow,
        Guid deviceId,
        CancellationToken ct)
    {
        var device = deviceId == Guid.Empty
            ? null
            : await uow.Devices.GetByIdAsync(deviceId, ct);
        return AuthenticateDevice(request, configuration, device);
    }

    private static (IActionResult? Denied, AuthenticatedEdgeDevice? Identity) AuthenticateDevice(
        HttpRequest request,
        IConfiguration configuration,
        Domain.Entities.Device? device)
    {
        if (device is null)
            return (new NotFoundObjectResult(new { success = false, message = "Device không tồn tại." }), null);
        if (device.EdgeState is not ("Approved" or "Online")
            || device.Status == Domain.Enums.DeviceStatus.Maintenance)
            return (new UnauthorizedObjectResult(new { success = false, message = "Device credential đã bị vô hiệu hóa." }), null);

        var suppliedDeviceKey = request.Headers[DeviceHeaderName].FirstOrDefault()?.Trim();
        if (!string.IsNullOrWhiteSpace(device.CredentialHash))
        {
            if (string.IsNullOrWhiteSpace(suppliedDeviceKey))
                return (new UnauthorizedObjectResult(new { success = false, message = "X-Device-Key là bắt buộc." }), null);
            var suppliedHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(suppliedDeviceKey)));
            if (!FixedTimeEquals(suppliedHash, device.CredentialHash))
                return (new UnauthorizedObjectResult(new { success = false, message = "X-Device-Key không hợp lệ." }), null);
            return (null, new AuthenticatedEdgeDevice(
                device.Id, device.DeviceCode, $"device:{device.Id:N}", false));
        }

        if (!configuration.GetValue("EdgeSync:AllowLegacySharedKey", true))
            return (new UnauthorizedObjectResult(new
            {
                success = false,
                message = "Legacy edge credential đã bị tắt; hãy provision credential riêng."
            }), null);
        var denied = Check(request, configuration);
        return denied is null
            ? (null, new AuthenticatedEdgeDevice(
                device.Id, device.DeviceCode, $"legacy:{device.Id:N}", true))
            : (denied, null);
    }

    private static bool FixedTimeEquals(string supplied, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied),
            Encoding.UTF8.GetBytes(expected));
}
