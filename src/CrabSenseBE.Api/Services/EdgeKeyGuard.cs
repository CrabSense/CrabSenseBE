using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Services;

/// <summary>
/// Xác thực request từ edge (kiosk/ESP32) bằng header X-Edge-Key.
/// Thiếu cấu hình EdgeSync:ApiKey thì ĐÓNG endpoint (fail-closed) — không được cho qua,
/// vì deploy thiếu config sẽ biến endpoint thành public.
/// </summary>
public static class EdgeKeyGuard
{
    public const string HeaderName = "X-Edge-Key";
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

    private static bool FixedTimeEquals(string supplied, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied),
            Encoding.UTF8.GetBytes(expected));
}
