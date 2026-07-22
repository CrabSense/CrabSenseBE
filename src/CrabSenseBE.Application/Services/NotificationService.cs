using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Options;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Notification Center: in-app + Telegram / Zalo OA / push / email delivery.
/// Telegram & Zalo gọi HTTP thật khi ConfigJson đủ; thiếu config → status=skipped.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IUnitOfWork _uow;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NotificationService> _logger;
    private readonly FcmOptions _fcm;

    public NotificationService(
        IUnitOfWork uow,
        IHttpClientFactory httpClientFactory,
        ILogger<NotificationService> logger,
        IOptions<FcmOptions> fcm)
    {
        _uow = uow;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _fcm = fcm.Value;
    }

    public async Task<ApiResponse<IEnumerable<NotificationDto>>> GetByUserAsync(
        Guid userId, bool unreadOnly = false, CancellationToken ct = default)
    {
        var list = await _uow.Notifications.FindAsync(
            n => n.UserId == userId && (!unreadOnly || !n.IsRead), ct);
        return ApiResponse<IEnumerable<NotificationDto>>.Ok(
            list.OrderByDescending(n => n.CreatedAt).Select(Map));
    }

    public async Task<ApiResponse> MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var n = await _uow.Notifications.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Notification");
        n.IsRead = true;
        n.ReadAt = DateTime.UtcNow;
        _uow.Notifications.Update(n);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Marked as read.");
    }

    public async Task<ApiResponse<IEnumerable<NotificationChannelDto>>> GetChannelsAsync(CancellationToken ct = default)
    {
        await EnsureDefaultChannelsAsync(ct);
        var channels = await _uow.NotificationChannels.GetAllAsync(ct);
        return ApiResponse<IEnumerable<NotificationChannelDto>>.Ok(channels.Select(MapChannel));
    }

    public async Task<ApiResponse<NotificationChannelDto>> GetChannelAsync(Guid id, CancellationToken ct = default)
    {
        var ch = await _uow.NotificationChannels.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("NotificationChannel");
        return ApiResponse<NotificationChannelDto>.Ok(MapChannel(ch));
    }

    public async Task<ApiResponse<NotificationChannelDto>> CreateChannelAsync(
        CreateNotificationChannelRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.ChannelCode))
            throw AppException.BadRequest("channelCode is required.");
        var code = req.ChannelCode.Trim().ToLowerInvariant();
        if (await _uow.NotificationChannels.AnyAsync(c => c.ChannelCode == code, ct))
            throw AppException.Conflict($"Channel '{code}' already exists.");

        var ch = new NotificationChannel
        {
            ChannelCode = code,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? code : req.DisplayName.Trim(),
            IsEnabled = req.IsEnabled,
            ConfigJson = req.ConfigJson
        };
        await _uow.NotificationChannels.AddAsync(ch, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<NotificationChannelDto>.Ok(MapChannel(ch), "Channel created.");
    }

    public async Task<ApiResponse<NotificationChannelDto>> UpdateChannelAsync(
        Guid id, UpdateNotificationChannelRequest req, CancellationToken ct = default)
    {
        var ch = await _uow.NotificationChannels.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("NotificationChannel");
        ch.IsEnabled = req.IsEnabled;
        if (req.ConfigJson is not null)
            ch.ConfigJson = req.ConfigJson;
        if (req.DisplayName is not null)
            ch.DisplayName = req.DisplayName.Trim();
        _uow.NotificationChannels.Update(ch);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<NotificationChannelDto>.Ok(MapChannel(ch));
    }

    public async Task<ApiResponse> DeleteChannelAsync(Guid id, CancellationToken ct = default)
    {
        var ch = await _uow.NotificationChannels.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("NotificationChannel");
        _uow.NotificationChannels.Remove(ch);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Channel deleted.");
    }

    public async Task<ApiResponse> TestChannelAsync(
        Guid id, TestNotificationChannelRequest req, CancellationToken ct = default)
    {
        var ch = await _uow.NotificationChannels.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("NotificationChannel");
        var title = string.IsNullOrWhiteSpace(req.Title) ? "CrabSense test" : req.Title!;
        var body = string.IsNullOrWhiteSpace(req.Body)
            ? $"Test channel {ch.ChannelCode} at {DateTime.UtcNow:O}"
            : req.Body!;

        var (ok, detail) = await DispatchChannelAsync(ch, title, body, null, ct);
        return ok
            ? ApiResponse.Ok($"Test OK via {ch.ChannelCode}: {detail}")
            : throw AppException.BadRequest($"Test failed via {ch.ChannelCode}: {detail}");
    }

    public async Task<ApiResponse<PushTokenDto>> RegisterPushTokenAsync(
        Guid userId, RegisterPushTokenRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Token))
            throw AppException.BadRequest("token is required.");

        var token = req.Token.Trim();
        var platform = string.IsNullOrWhiteSpace(req.Platform) ? "android" : req.Platform.Trim().ToLowerInvariant();
        var existing = (await _uow.UserPushTokens.FindAsync(
            t => t.UserId == userId && t.Token == token, ct)).FirstOrDefault();

        if (existing is not null)
        {
            existing.IsActive = true;
            existing.Platform = platform;
            existing.DeviceId = req.DeviceId?.Trim();
            existing.LastSeenAt = DateTime.UtcNow;
            _uow.UserPushTokens.Update(existing);
            await _uow.SaveChangesAsync(ct);
            return ApiResponse<PushTokenDto>.Ok(MapPushToken(existing), "Token updated.");
        }

        if (!string.IsNullOrWhiteSpace(req.DeviceId))
        {
            var sameDevice = (await _uow.UserPushTokens.FindAsync(
                t => t.UserId == userId && t.DeviceId == req.DeviceId.Trim(), ct)).ToList();
            foreach (var old in sameDevice)
            {
                old.IsActive = false;
                _uow.UserPushTokens.Update(old);
            }
        }

        var entity = new UserPushToken
        {
            UserId = userId,
            Token = token,
            Platform = platform,
            DeviceId = req.DeviceId?.Trim(),
            IsActive = true,
            LastSeenAt = DateTime.UtcNow
        };
        await _uow.UserPushTokens.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<PushTokenDto>.Ok(MapPushToken(entity), "Token registered.");
    }

    public async Task<ApiResponse> UnregisterPushTokenAsync(
        Guid userId, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw AppException.BadRequest("token is required.");

        var entity = (await _uow.UserPushTokens.FindAsync(
            t => t.UserId == userId && t.Token == token.Trim(), ct)).FirstOrDefault()
            ?? throw AppException.NotFound("PushToken");

        entity.IsActive = false;
        _uow.UserPushTokens.Update(entity);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Token unregistered.");
    }

    public async Task<ApiResponse<IEnumerable<PushTokenDto>>> GetPushTokensAsync(
        Guid userId, CancellationToken ct = default)
    {
        var items = await _uow.UserPushTokens.FindAsync(t => t.UserId == userId && t.IsActive, ct);
        return ApiResponse<IEnumerable<PushTokenDto>>.Ok(items.Select(MapPushToken));
    }

    public async Task<ApiResponse<NotificationSettingsDto>> GetSettingsAsync(CancellationToken ct = default)
    {
        await EnsureDefaultChannelsAsync(ct);
        var channels = (await _uow.NotificationChannels.GetAllAsync(ct)).ToList();
        return ApiResponse<NotificationSettingsDto>.Ok(BuildSettings(channels));
    }

    public async Task<ApiResponse<NotificationSettingsDto>> UpdateSettingsAsync(
        UpdateNotificationSettingsRequest req, CancellationToken ct = default)
    {
        await EnsureDefaultChannelsAsync(ct);
        var channels = (await _uow.NotificationChannels.GetAllAsync(ct)).ToList();

        if (req.PushEnabled is not null)
            await UpsertChannelSettingAsync(channels, "push", "Mobile Push", req.PushEnabled.Value, null, ct);

        if (req.Telegram is not null)
            await UpsertChannelSettingAsync(
                channels, "telegram", "Telegram", req.Telegram.Enabled, req.Telegram.ConfigJson, ct);

        if (req.Zalo is not null)
            await UpsertChannelSettingAsync(
                channels, "zalo_oa", "Zalo OA", req.Zalo.Enabled, req.Zalo.ConfigJson, ct);

        channels = (await _uow.NotificationChannels.GetAllAsync(ct)).ToList();
        return ApiResponse<NotificationSettingsDto>.Ok(BuildSettings(channels), "Settings updated.");
    }

    public async Task NotifyUsersAsync(
        IEnumerable<Guid> userIds, string title, string body, Guid? alertId = null, CancellationToken ct = default)
    {
        await EnsureDefaultChannelsAsync(ct);
        var channels = (await _uow.NotificationChannels.FindAsync(c => c.IsEnabled, ct)).ToList();
        var userList = userIds.Distinct().ToList();
        if (userList.Count == 0) return;

        Notification? anchor = null;
        foreach (var userId in userList)
        {
            var notification = new Notification
            {
                UserId = userId,
                AlertId = alertId,
                Title = title,
                Body = body,
                Channel = "in-app"
            };
            await _uow.Notifications.AddAsync(notification, ct);
            anchor ??= notification;

            var pushChannel = channels.FirstOrDefault(c => c.ChannelCode == "push");
            if (pushChannel is not null)
            {
                var (ok, detail) = await DispatchChannelAsync(pushChannel, title, body, userId, ct);
                await _uow.NotificationDeliveries.AddAsync(new NotificationDelivery
                {
                    NotificationId = notification.Id,
                    ChannelCode = pushChannel.ChannelCode,
                    Recipient = userId.ToString(),
                    Status = ok ? "sent" : "failed",
                    ErrorMessage = ok ? null : detail,
                    SentAt = ok ? DateTime.UtcNow : null
                }, ct);
            }
        }

        if (anchor is not null)
        {
            foreach (var ch in channels.Where(c => c.ChannelCode is "telegram" or "zalo_oa"))
            {
                var (ok, detail) = await DispatchChannelAsync(ch, title, body, null, ct);
                await _uow.NotificationDeliveries.AddAsync(new NotificationDelivery
                {
                    NotificationId = anchor.Id,
                    ChannelCode = ch.ChannelCode,
                    Recipient = ExtractRecipient(ch.ConfigJson),
                    Status = ok ? "sent" : "failed",
                    ErrorMessage = ok ? null : detail,
                    SentAt = ok ? DateTime.UtcNow : null
                }, ct);
            }
        }

        await _uow.SaveChangesAsync(ct);
    }

    private async Task UpsertChannelSettingAsync(
        List<NotificationChannel> channels,
        string code,
        string displayName,
        bool enabled,
        string? configJson,
        CancellationToken ct)
    {
        var ch = channels.FirstOrDefault(c => c.ChannelCode == code);
        if (ch is null)
        {
            ch = new NotificationChannel
            {
                ChannelCode = code,
                DisplayName = displayName,
                IsEnabled = enabled,
                ConfigJson = configJson
            };
            await _uow.NotificationChannels.AddAsync(ch, ct);
            await _uow.SaveChangesAsync(ct);
            return;
        }

        ch.IsEnabled = enabled;
        if (configJson is not null)
            ch.ConfigJson = configJson;
        _uow.NotificationChannels.Update(ch);
        await _uow.SaveChangesAsync(ct);
    }

    private static NotificationSettingsDto BuildSettings(IEnumerable<NotificationChannel> channels)
    {
        var list = channels.ToList();
        NotificationChannelSettingDto Map(string code) =>
            list.Where(c => c.ChannelCode == code).Select(c => new NotificationChannelSettingDto(c.IsEnabled, c.ConfigJson))
                .FirstOrDefault() ?? new NotificationChannelSettingDto(false, null);

        return new NotificationSettingsDto(
            Map("push").Enabled,
            Map("telegram"),
            Map("zalo_oa"));
    }

    private async Task<(bool Ok, string Detail)> DispatchChannelAsync(
        NotificationChannel ch, string title, string body, Guid? userId, CancellationToken ct)
    {
        try
        {
            return ch.ChannelCode switch
            {
                "telegram" => await SendTelegramAsync(ch.ConfigJson, title, body, ct),
                "zalo_oa" => await SendZaloAsync(ch.ConfigJson, title, body, ct),
                "push" when userId is null => (false, "userId required for push"),
                "push" => await SendPushAsync(userId!.Value, title, body, ct),
                "email" => (true, "queued-stub (provider not configured)"),
                _ => (true, "noop")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dispatch {Channel} failed", ch.ChannelCode);
            return (false, ex.Message);
        }
    }

    private async Task<(bool Ok, string Detail)> SendTelegramAsync(
        string? configJson, string title, string body, CancellationToken ct)
    {
        var (botToken, chatId) = ParseTelegramConfig(configJson);
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            return (false, "ConfigJson needs bot_token + chat_id");

        var client = _httpClientFactory.CreateClient("telegram");
        var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
        var payload = new
        {
            chat_id = chatId,
            text = $"*{EscapeMd(title)}*\n{EscapeMd(body)}",
            parse_mode = "Markdown"
        };
        using var res = await client.PostAsJsonAsync(url, payload, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return (false, $"HTTP {(int)res.StatusCode}: {raw}");
        return (true, "telegram sent");
    }

    private async Task<(bool Ok, string Detail)> SendZaloAsync(
        string? configJson, string title, string body, CancellationToken ct)
    {
        // Zalo OA official send requires access_token + user_id; message template varies by OA setup.
        var (accessToken, userId) = ParseZaloConfig(configJson);
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(userId))
            return (false, "ConfigJson needs access_token + user_id");

        var client = _httpClientFactory.CreateClient("zalo");
        var url = $"https://openapi.zalo.me/v3.0/oa/message/cs?access_token={Uri.EscapeDataString(accessToken)}";
        var payload = new
        {
            recipient = new { user_id = userId },
            message = new { text = $"{title}\n{body}" }
        };
        using var res = await client.PostAsJsonAsync(url, payload, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return (false, $"HTTP {(int)res.StatusCode}: {raw}");
        return (true, "zalo sent");
    }

    private async Task<(bool Ok, string Detail)> SendPushAsync(
        Guid userId, string title, string body, CancellationToken ct)
    {
        var tokens = (await _uow.UserPushTokens.FindAsync(
            t => t.UserId == userId && t.IsActive, ct)).ToList();
        if (tokens.Count == 0)
            return (false, "no registered device tokens");

        if (!_fcm.Enabled || string.IsNullOrWhiteSpace(_fcm.ServerKey))
            return (true, $"queued-stub ({tokens.Count} token(s), FCM not configured)");

        var client = _httpClientFactory.CreateClient("fcm");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"key={_fcm.ServerKey}");

        var sent = 0;
        var errors = new List<string>();
        foreach (var token in tokens)
        {
            var payload = new
            {
                to = token.Token,
                notification = new { title, body },
                data = new { title, body, userId = userId.ToString() }
            };
            using var res = await client.PostAsJsonAsync("https://fcm.googleapis.com/fcm/send", payload, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode)
            {
                sent++;
                token.LastSeenAt = DateTime.UtcNow;
                _uow.UserPushTokens.Update(token);
            }
            else
            {
                errors.Add($"{token.Platform}: HTTP {(int)res.StatusCode}");
                _logger.LogWarning("FCM push failed for user {UserId}: {Raw}", userId, raw);
            }
        }

        if (sent == 0)
            return (false, string.Join("; ", errors));
        return (true, $"push sent to {sent}/{tokens.Count} device(s)");
    }

    private async Task EnsureDefaultChannelsAsync(CancellationToken ct)
    {
        if (await _uow.NotificationChannels.AnyAsync(_ => true, ct)) return;

        var defaults = new[]
        {
            new NotificationChannel { ChannelCode = "in_app", DisplayName = "In-App (Web/Mobile)", IsEnabled = true },
            new NotificationChannel
            {
                ChannelCode = "telegram",
                DisplayName = "Telegram",
                IsEnabled = false,
                ConfigJson = """{"bot_token":"","chat_id":""}"""
            },
            new NotificationChannel
            {
                ChannelCode = "zalo_oa",
                DisplayName = "Zalo OA",
                IsEnabled = false,
                ConfigJson = """{"access_token":"","user_id":""}"""
            },
            new NotificationChannel { ChannelCode = "push", DisplayName = "Mobile Push", IsEnabled = false },
            new NotificationChannel { ChannelCode = "email", DisplayName = "Email", IsEnabled = false }
        };
        foreach (var d in defaults)
            await _uow.NotificationChannels.AddAsync(d, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private static (string? BotToken, string? ChatId) ParseTelegramConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var token = root.TryGetProperty("bot_token", out var t) ? t.GetString() : null;
            var chat = root.TryGetProperty("chat_id", out var c) ? c.ToString() : null;
            return (token, chat);
        }
        catch { return (null, null); }
    }

    private static (string? AccessToken, string? UserId) ParseZaloConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var token = root.TryGetProperty("access_token", out var t) ? t.GetString() : null;
            var uid = root.TryGetProperty("user_id", out var u) ? u.ToString() : null;
            return (token, uid);
        }
        catch { return (null, null); }
    }

    private static string? ExtractRecipient(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("chat_id", out var chat))
                return chat.ToString();
            if (doc.RootElement.TryGetProperty("user_id", out var uid))
                return uid.ToString();
        }
        catch { /* ignore */ }
        return null;
    }

    private static string EscapeMd(string s) =>
        s.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[").Replace("`", "\\`");

    private static NotificationDto Map(Notification n) =>
        new(n.Id, n.UserId, n.AlertId, n.Title, n.Body, n.Channel, n.IsRead, n.CreatedAt);

    private static NotificationChannelDto MapChannel(NotificationChannel c) =>
        new(c.Id, c.ChannelCode, c.DisplayName, c.IsEnabled, c.ConfigJson);

    private static PushTokenDto MapPushToken(UserPushToken t) =>
        new(t.Id, t.UserId, t.Token, t.Platform, t.DeviceId, t.IsActive, t.LastSeenAt);
}
