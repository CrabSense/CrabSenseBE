using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Alert;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>Thông báo in-app / telegram / zalo (dispatch stub có log delivery).</summary>
public interface INotificationService
{
    Task<ApiResponse<IEnumerable<NotificationDto>>> GetByUserAsync(Guid userId, bool unreadOnly = false, CancellationToken ct = default);
    Task<ApiResponse<AlertUnreadCountDto>> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);
    Task<ApiResponse> MarkReadAsync(
        Guid id, Guid userId, bool isSystemAdmin = false, CancellationToken ct = default);
    Task<ApiResponse> MarkAllReadAsync(Guid userId, CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<NotificationChannelDto>>> GetChannelsAsync(CancellationToken ct = default);
    Task<ApiResponse<NotificationChannelDto>> GetChannelAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<NotificationChannelDto>> CreateChannelAsync(CreateNotificationChannelRequest req, CancellationToken ct = default);
    Task<ApiResponse<NotificationChannelDto>> UpdateChannelAsync(Guid id, UpdateNotificationChannelRequest req, CancellationToken ct = default);
    Task<ApiResponse> DeleteChannelAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse> TestChannelAsync(Guid id, TestNotificationChannelRequest req, CancellationToken ct = default);

    Task<ApiResponse<PushTokenDto>> RegisterPushTokenAsync(
        Guid userId, RegisterPushTokenRequest req, CancellationToken ct = default);
    Task<ApiResponse> UnregisterPushTokenAsync(
        Guid userId, string token, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<PushTokenDto>>> GetPushTokensAsync(
        Guid userId, CancellationToken ct = default);

    Task<ApiResponse<NotificationSettingsDto>> GetSettingsAsync(CancellationToken ct = default);
    Task<ApiResponse<NotificationSettingsDto>> UpdateSettingsAsync(
        UpdateNotificationSettingsRequest req, CancellationToken ct = default);

    /// <summary>Danh sách quy tắc thông báo (seed mặc định nếu DB trống).</summary>
    Task<ApiResponse<IEnumerable<NotificationRuleDto>>> GetRulesAsync(CancellationToken ct = default);

    /// <summary>Cập nhật quy tắc theo TypeCode — admin bật/tắt, đổi người nhận, kênh, mẫu.</summary>
    Task<ApiResponse<NotificationRuleDto>> UpdateRuleAsync(
        string typeCode, UpdateNotificationRuleRequest req, CancellationToken ct = default);

    /// <summary>Render thử mẫu tiêu đề/nội dung với token mẫu (không gửi).</summary>
    Task<ApiResponse<NotificationRulePreviewDto>> PreviewRuleAsync(
        string typeCode, IReadOnlyDictionary<string, string?> tokens, CancellationToken ct = default);

    /// <summary>Log gửi thực tế — lọc theo status (sent|failed|pending).</summary>
    Task<ApiResponse<IEnumerable<NotificationDeliveryDto>>> GetDeliveriesAsync(
        string? status = null, int take = 100, CancellationToken ct = default);

    /// <summary>
    /// Tạo notification theo NotificationRule: lọc người nhận, render mẫu, gửi kênh được cấu hình.
    /// Rule không tồn tại → giữ hành vi cũ (mọi role trại, mọi kênh đang bật).
    /// </summary>
    Task NotifyAsync(
        string typeCode,
        string message,
        Guid? alertId = null,
        Guid? areaId = null,
        string? severity = null,
        IReadOnlyDictionary<string, string?>? tokens = null,
        CancellationToken ct = default);
}
