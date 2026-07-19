using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.Logging;

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

    public NotificationService(
        IUnitOfWork uow,
        IHttpClientFactory httpClientFactory,
        ILogger<NotificationService> logger)
    {
        _uow = uow;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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

        var (ok, detail) = await DispatchChannelAsync(ch, title, body, ct);
        return ok
            ? ApiResponse.Ok($"Test OK via {ch.ChannelCode}: {detail}")
            : throw AppException.BadRequest($"Test failed via {ch.ChannelCode}: {detail}");
    }

    public async Task NotifyUsersAsync(
        IEnumerable<Guid> userIds, string title, string body, Guid? alertId = null, CancellationToken ct = default)
    {
        await EnsureDefaultChannelsAsync(ct);
        var channels = (await _uow.NotificationChannels.FindAsync(c => c.IsEnabled, ct)).ToList();

        foreach (var userId in userIds.Distinct())
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
            await _uow.SaveChangesAsync(ct);

            foreach (var ch in channels)
            {
                if (ch.ChannelCode is "in_app" or "in-app") continue;

                var (ok, detail) = await DispatchChannelAsync(ch, title, body, ct);
                var delivery = new NotificationDelivery
                {
                    NotificationId = notification.Id,
                    ChannelCode = ch.ChannelCode,
                    Recipient = ExtractRecipient(ch.ConfigJson),
                    Status = ok ? "sent" : "failed",
                    ErrorMessage = ok ? null : detail,
                    SentAt = ok ? DateTime.UtcNow : null
                };
                await _uow.NotificationDeliveries.AddAsync(delivery, ct);
            }
        }

        await _uow.SaveChangesAsync(ct);
    }

    private async Task<(bool Ok, string Detail)> DispatchChannelAsync(
        NotificationChannel ch, string title, string body, CancellationToken ct)
    {
        try
        {
            return ch.ChannelCode switch
            {
                "telegram" => await SendTelegramAsync(ch.ConfigJson, title, body, ct),
                "zalo_oa" => await SendZaloAsync(ch.ConfigJson, title, body, ct),
                "push" or "email" => (true, "queued-stub (provider not configured)"),
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
}
