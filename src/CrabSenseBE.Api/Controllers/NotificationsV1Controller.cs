using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>Mobile v1 aliases — tương thích ApiConstants Flutter.</summary>
[ApiController]
[Authorize]
[Route("api/v1/notifications")]
[Tags("11b. Notifications v1 (Mobile)")]
[Produces("application/json")]
public class NotificationsV1Controller : ControllerBase
{
    private readonly INotificationService _service;
    private readonly ICurrentUserService _currentUser;

    public NotificationsV1Controller(INotificationService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    /// <summary>[CREATE] Đăng ký FCM token — alias Mobile</summary>
    [HttpPost("register")]
    public async Task<IActionResult> RegisterPushToken(
        [FromBody] RegisterPushTokenRequest req, CancellationToken ct)
        => Ok(await _service.RegisterPushTokenAsync(_currentUser.UserId, req, ct));

    /// <summary>[DELETE] Hủy đăng ký FCM token</summary>
    [HttpDelete("register")]
    public async Task<IActionResult> UnregisterPushToken([FromQuery] string token, CancellationToken ct)
        => Ok(await _service.UnregisterPushTokenAsync(_currentUser.UserId, token, ct));

    /// <summary>[READ] Cấu hình thông báo Telegram / Zalo / Push</summary>
    [HttpGet("settings")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
        => Ok(await _service.GetSettingsAsync(ct));

    /// <summary>[UPDATE] Cấu hình thông báo Telegram / Zalo / Push</summary>
    [HttpPut("settings")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateNotificationSettingsRequest req, CancellationToken ct)
        => Ok(await _service.UpdateSettingsAsync(req, ct));
}
