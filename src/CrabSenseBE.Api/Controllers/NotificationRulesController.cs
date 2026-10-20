using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Admin cấu hình thông báo: loại nào gửi, cho ai, qua kênh nào, nội dung ra sao.
/// Swagger: /swagger → "11c. Notification rules".
/// </summary>
[ApiController]
[Route("api/notification-rules")]
[Authorize(Roles = AppRoles.Platform)]
[Tags("11c. Notification rules (Admin)")]
[Produces("application/json")]
public class NotificationRulesController : ControllerBase
{
    private readonly INotificationService _service;

    public NotificationRulesController(INotificationService service) => _service = service;

    /// <summary>[READ] Danh sách quy tắc (seed mặc định nếu DB trống)</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetRulesAsync(ct));

    /// <summary>[UPDATE] Bật/tắt, đổi role nhận, kênh gửi, mẫu tiêu đề/nội dung</summary>
    [HttpPut("{typeCode}")]
    public async Task<IActionResult> Update(
        string typeCode, [FromBody] UpdateNotificationRuleRequest req, CancellationToken ct)
        => Ok(await _service.UpdateRuleAsync(typeCode, req, ct));

    /// <summary>[ACTION] Render thử mẫu với token mẫu, không gửi thật</summary>
    [HttpPost("{typeCode}/preview")]
    public async Task<IActionResult> Preview(
        string typeCode,
        [FromBody] Dictionary<string, string?>? tokens,
        CancellationToken ct)
        => Ok(await _service.PreviewRuleAsync(typeCode, tokens ?? new Dictionary<string, string?>(), ct));
}
