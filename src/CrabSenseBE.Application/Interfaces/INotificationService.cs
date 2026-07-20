using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Alert;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>Thông báo in-app / telegram / zalo (dispatch stub có log delivery).</summary>
public interface INotificationService
{
    Task<ApiResponse<IEnumerable<NotificationDto>>> GetByUserAsync(Guid userId, bool unreadOnly = false, CancellationToken ct = default);
    Task<ApiResponse> MarkReadAsync(Guid id, CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<NotificationChannelDto>>> GetChannelsAsync(CancellationToken ct = default);
    Task<ApiResponse<NotificationChannelDto>> GetChannelAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<NotificationChannelDto>> CreateChannelAsync(CreateNotificationChannelRequest req, CancellationToken ct = default);
    Task<ApiResponse<NotificationChannelDto>> UpdateChannelAsync(Guid id, UpdateNotificationChannelRequest req, CancellationToken ct = default);
    Task<ApiResponse> DeleteChannelAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse> TestChannelAsync(Guid id, TestNotificationChannelRequest req, CancellationToken ct = default);

    /// <summary>Tạo notification + ghi delivery theo các kênh đang bật.</summary>
    Task NotifyUsersAsync(IEnumerable<Guid> userIds, string title, string body, Guid? alertId = null, CancellationToken ct = default);
}
