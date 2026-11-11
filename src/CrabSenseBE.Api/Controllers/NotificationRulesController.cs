using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Quy tắc cảnh báo môi trường thuộc Farm Administrator.
/// Quy tắc thông báo kỹ thuật thuộc System Administrator.
/// Kênh Telegram/Zalo vẫn ở /api/notifications/channels.
/// </summary>
[ApiController]
[Route("api/notification-rules")]
[Authorize(Roles = AppRoles.FarmManageWithPlatform)]
[Tags("11c. Notification rules (Admin)")]
[Produces("application/json")]
public class NotificationRulesController : ControllerBase
{
    private readonly INotificationService _service;

    public NotificationRulesController(INotificationService service) => _service = service;

    /// <summary>[READ] Quy tắc trong phạm vi role: môi trường hoặc kỹ thuật.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var response = await _service.GetRulesAsync(ct);
        var data = (response.Data ?? Enumerable.Empty<NotificationRuleDto>())
            .Where(rule => CanAccess(rule.TypeCode));
        return Ok(ApiResponse<IEnumerable<NotificationRuleDto>>.Ok(data, response.Message));
    }

    /// <summary>[UPDATE] Quy tắc môi trường: bật/tắt, mức, role nhận. Kênh và mẫu thuộc System Administrator.</summary>
    [HttpPut("{typeCode}")]
    public async Task<IActionResult> Update(
        string typeCode, [FromBody] UpdateNotificationRuleRequest req, CancellationToken ct)
    {
        if (!CanAccess(typeCode))
            return Forbid();
        return Ok(await _service.UpdateRuleAsync(typeCode, req, ct));
    }

    /// <summary>[ACTION] Render thử mẫu với token mẫu, không gửi thật</summary>
    [HttpPost("{typeCode}/preview")]
    public async Task<IActionResult> Preview(
        string typeCode,
        [FromBody] Dictionary<string, string?>? tokens,
        CancellationToken ct)
    {
        if (!CanAccess(typeCode))
            return Forbid();
        return Ok(await _service.PreviewRuleAsync(typeCode, tokens ?? new Dictionary<string, string?>(), ct));
    }

    /// <summary>Ngưỡng, tràn bể, cạn bể. Phần còn lại là quy tắc kỹ thuật.</summary>
    private bool CanAccess(string typeCode)
    {
        var environmental = NotificationTypeCodes.IsWarningType(typeCode.Trim().ToLowerInvariant());
        if (environmental)
            return User.IsInRole(AppRoles.FarmAdministrator) || User.IsInRole("FarmOwner");
        return User.IsInRole(AppRoles.SystemAdministrator) || User.IsInRole("SystemAdmin");
    }
}
