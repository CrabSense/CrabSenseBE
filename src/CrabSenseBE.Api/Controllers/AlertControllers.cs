using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/alerts")]
[Authorize]
[Tags("10. Alerts")]
[Produces("application/json")]
public class AlertsController : ControllerBase
{
    private readonly IAlertService _service;
    public AlertsController(IAlertService service) => _service = service;

    /// <summary>[READ] List alerts</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? activeOnly = true,
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] Guid? boxId = null,
        CancellationToken ct = default)
        => Ok(await _service.GetAlertsAsync(activeOnly, farmingAreaId, boxId, ct));

    /// <summary>[READ] Unread / active alert count (Mobile)</summary>
    [HttpGet("unread/count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
        => Ok(await _service.GetUnreadCountAsync(ct));

    /// <summary>[READ] Resolved / acknowledged alert history (Mobile)</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] int days = 30,
        [FromQuery] Guid? farmingAreaId = null,
        CancellationToken ct = default)
        => Ok(await _service.GetHistoryAsync(days, farmingAreaId, ct));

    /// <summary>[READ] Alert by id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    /// <summary>[UPDATE] Acknowledge alert</summary>
    [HttpPatch("{id:guid}/acknowledge")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Acknowledge(Guid id, [FromBody] AcknowledgeAlertRequest? req, CancellationToken ct = default)
        => Ok(await _service.AcknowledgeAsync(id, req ?? new AcknowledgeAlertRequest(null), ct));

    /// <summary>[UPDATE] Acknowledge alias for Mobile clients using POST</summary>
    [HttpPost("{id:guid}/acknowledge")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> AcknowledgePost(Guid id, [FromBody] AcknowledgeAlertRequest? req, CancellationToken ct = default)
        => Ok(await _service.AcknowledgeAsync(id, req ?? new AcknowledgeAlertRequest(null), ct));

    /// <summary>[UPDATE] Bắt đầu xử lý cảnh báo.</summary>
    [HttpPost("{id:guid}/process")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> StartProcessing(
        Guid id, [FromBody] StartProcessingRequest? req, CancellationToken ct)
        => Ok(await _service.StartProcessingAsync(id, req, ct));

    /// <summary>[UPDATE] Resolve alert</summary>
    [HttpPatch("{id:guid}/resolve")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Resolve(
        Guid id, [FromBody] ResolveAlertRequest? req, CancellationToken ct)
        => Ok(await _service.ResolveAsync(id, req, ct));

    /// <summary>[UPDATE] Resolve / dismiss alias (POST)</summary>
    [HttpPost("{id:guid}/resolve")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> ResolvePost(
        Guid id, [FromBody] ResolveAlertRequest? req, CancellationToken ct)
        => Ok(await _service.ResolveAsync(id, req, ct));

    /// <summary>[ACTION] Scan disconnected devices/sensors</summary>
    [HttpPost("check-disconnects")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> CheckDisconnects([FromQuery] int timeoutMinutes = 15, CancellationToken ct = default)
        => Ok(await _service.CheckDisconnectsAsync(timeoutMinutes, ct));
}

[ApiController]
[Route("api/alert-thresholds")]
[Authorize]
[Tags("10. Alerts")]
[Produces("application/json")]
public class AlertThresholdsController : ControllerBase
{
    private readonly IAlertService _service;
    public AlertThresholdsController(IAlertService service) => _service = service;

    /// <summary>[READ] List all thresholds</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetThresholdsAsync(ct));

    /// <summary>[CREATE] Create alert threshold</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create([FromBody] CreateAlertThresholdRequest req, CancellationToken ct)
        => Ok(await _service.CreateThresholdAsync(req, ct));

    /// <summary>[UPDATE] Update alert threshold</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAlertThresholdRequest req, CancellationToken ct)
        => Ok(await _service.UpdateThresholdAsync(id, req, ct));

    /// <summary>[DELETE] Delete alert threshold</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteThresholdAsync(id, ct));
}

[ApiController]
[Route("api/notifications")]
[Authorize]
[Tags("11. Notifications")]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _service;
    private readonly ICurrentUserService _currentUser;
    public NotificationsController(INotificationService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    /// <summary>[READ] Notifications by user</summary>
    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> GetByUser(Guid userId, [FromQuery] bool unreadOnly = false, CancellationToken ct = default)
        => Ok(await _service.GetByUserAsync(userId, unreadOnly, ct));

    /// <summary>[UPDATE] Mark notification as read</summary>
    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
        => Ok(await _service.MarkReadAsync(id, ct));

    /// <summary>[READ] Notification channels (Telegram/Zalo/...)</summary>
    [HttpGet("channels")]
    public async Task<IActionResult> GetChannels(CancellationToken ct)
        => Ok(await _service.GetChannelsAsync(ct));

    /// <summary>[READ] Channel by id</summary>
    [HttpGet("channels/{id:guid}")]
    public async Task<IActionResult> GetChannel(Guid id, CancellationToken ct)
        => Ok(await _service.GetChannelAsync(id, ct));

    /// <summary>[CREATE] Add notification channel</summary>
    [HttpPost("channels")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> CreateChannel([FromBody] CreateNotificationChannelRequest req, CancellationToken ct)
        => Ok(await _service.CreateChannelAsync(req, ct));

    /// <summary>[UPDATE] Enable/disable + configure channel (Telegram/Zalo/...)</summary>
    [HttpPut("channels/{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> UpdateChannel(Guid id, [FromBody] UpdateNotificationChannelRequest req, CancellationToken ct)
        => Ok(await _service.UpdateChannelAsync(id, req, ct));

    /// <summary>[DELETE] Remove notification channel</summary>
    [HttpDelete("channels/{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> DeleteChannel(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteChannelAsync(id, ct));

    /// <summary>[ACTION] Test send via channel</summary>
    [HttpPost("channels/{id:guid}/test")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> TestChannel(
        Guid id, [FromBody] TestNotificationChannelRequest? req, CancellationToken ct)
        => Ok(await _service.TestChannelAsync(id, req ?? new TestNotificationChannelRequest(), ct));

    /// <summary>[CREATE] Register FCM push token (Mobile)</summary>
    [HttpPost("register")]
    public async Task<IActionResult> RegisterPushToken(
        [FromBody] RegisterPushTokenRequest req, CancellationToken ct)
        => Ok(await _service.RegisterPushTokenAsync(_currentUser.UserId, req, ct));

    /// <summary>[DELETE] Unregister FCM push token</summary>
    [HttpDelete("register")]
    public async Task<IActionResult> UnregisterPushToken([FromQuery] string token, CancellationToken ct)
        => Ok(await _service.UnregisterPushTokenAsync(_currentUser.UserId, token, ct));

    /// <summary>[READ] Active push tokens for current user</summary>
    [HttpGet("register")]
    public async Task<IActionResult> GetPushTokens(CancellationToken ct)
        => Ok(await _service.GetPushTokensAsync(_currentUser.UserId, ct));

    /// <summary>[READ] Notification settings (push / Telegram / Zalo)</summary>
    [HttpGet("settings")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
        => Ok(await _service.GetSettingsAsync(ct));

    /// <summary>[UPDATE] Notification settings (push / Telegram / Zalo)</summary>
    [HttpPut("settings")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateNotificationSettingsRequest req, CancellationToken ct)
        => Ok(await _service.UpdateSettingsAsync(req, ct));
}
