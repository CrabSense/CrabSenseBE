using Google.Apis.Auth.OAuth2;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Options;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
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
    private readonly NotificationOptions _fallback;
    private readonly ICurrentUserService? _currentUser;

    public NotificationService(
        IUnitOfWork uow,
        IHttpClientFactory httpClientFactory,
        ILogger<NotificationService> logger,
        IOptions<FcmOptions> fcm,
        IOptions<NotificationOptions> fallback,
        ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _fcm = fcm.Value;
        _fallback = fallback.Value;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<IEnumerable<NotificationDto>>> GetByUserAsync(
        Guid userId, bool unreadOnly = false, CancellationToken ct = default)
    {
        var list = await _uow.Notifications.FindAsync(
            n => n.UserId == userId && n.Channel != "delivery" && (!unreadOnly || !n.IsRead), ct);
        return ApiResponse<IEnumerable<NotificationDto>>.Ok(
            list.OrderByDescending(n => n.CreatedAt).Select(Map));
    }

    public async Task<ApiResponse<AlertUnreadCountDto>> GetUnreadCountAsync(
        Guid userId, CancellationToken ct = default)
    {
        var count = await _uow.Notifications.CountAsync(n => n.UserId == userId && n.Channel != "delivery" && !n.IsRead, ct);
        return ApiResponse<AlertUnreadCountDto>.Ok(new AlertUnreadCountDto(count, count));
    }

    public async Task<ApiResponse> MarkReadAsync(
        Guid id, Guid userId, bool isSystemAdmin = false, CancellationToken ct = default)
    {
        var n = await _uow.Notifications.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Notification");

        // Notification là dữ liệu riêng của từng user: chỉ chủ sở hữu (hoặc SystemAdmin) được ghi.
        if (n.UserId != userId && !isSystemAdmin)
            throw AppException.NotFound("Notification");

        n.IsRead = true;
        n.ReadAt = DateTime.UtcNow;
        _uow.Notifications.Update(n);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Marked as read.");
    }

    public async Task<ApiResponse> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var unread = (await _uow.Notifications.FindAsync(
            n => n.UserId == userId && n.Channel != "delivery" && !n.IsRead, ct)).ToList();
        if (unread.Count == 0) return ApiResponse.Ok("No unread notifications.");

        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
            _uow.Notifications.Update(n);
        }
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok($"Marked {unread.Count} notification(s) as read.");
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
        if (!KnownChannelCodes.Contains(code)) throw AppException.BadRequest("Unsupported notification channel.");
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

        var userId = ch.ChannelCode == "push" && _currentUser?.IsAuthenticated == true ? _currentUser.UserId : (Guid?)null;
        var (ok, detail) = await DispatchChannelAsync(ch, title, body, userId, ct);
        if (userId.HasValue) await _uow.SaveChangesAsync(ct);
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
        if (token.Length > 1024 || token.Any(c => c < 33 || c > 126))
            throw AppException.BadRequest("Push token không hợp lệ.");
        var activeOwner = (await _uow.UserPushTokens.FindAsync(t => t.Token == token && t.IsActive && t.UserId != userId, ct)).FirstOrDefault();
        if (activeOwner is not null)
            throw AppException.Conflict("Push token đang thuộc tài khoản khác; hủy đăng ký hoặc làm mới token trên thiết bị.");
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

    public async Task<ApiResponse<IEnumerable<NotificationRuleDto>>> GetRulesAsync(CancellationToken ct = default)
    {
        var rules = await _uow.NotificationRules.GetAllAsync(ct);
        var areas = new FarmAreaScope(_uow, _currentUser);
        var actor = await areas.CurrentUserAsync(ct);
        if (actor is not null)
        {
            var allowed = await areas.AreaIdsAsync(ct);
            rules = actor.Role == UserRole.SystemAdministrator
                ? rules.Where(r => r.FarmingAreaId == null && !NotificationTypeCodes.IsWarningType(r.TypeCode))
                : rules.Where(r => NotificationTypeCodes.IsWarningType(r.TypeCode)
                    && (r.FarmingAreaId == null || allowed!.Contains(r.FarmingAreaId.Value)));
        }
        return ApiResponse<IEnumerable<NotificationRuleDto>>.Ok(
            rules.OrderBy(r => r.TypeCode).ThenBy(r => r.FarmingAreaId).Select(MapRule));
    }

    public async Task<ApiResponse<NotificationRuleDto>> UpdateRuleAsync(
        string typeCode, UpdateNotificationRuleRequest req, CancellationToken ct = default)
    {
        var code = NormalizeCode(typeCode);
        var environmental = NotificationTypeCodes.IsWarningType(code);
        var areas = new FarmAreaScope(_uow, _currentUser);
        var actor = await areas.CurrentUserAsync(ct);
        if (environmental && (req.ChannelCodes is not null || req.TitleTemplate is not null
            || req.BodyTemplate is not null || req.ScopeByArea == false))
            throw AppException.Forbidden("Kênh, mẫu và phạm vi an toàn không được thay đổi bởi Farm Administrator.");
        if (environmental)
        {
            if (req.FarmingAreaId is not Guid areaId || areaId == Guid.Empty)
                throw AppException.BadRequest("FarmingAreaId là bắt buộc khi sửa quy tắc môi trường.");
            await areas.EnsureAsync(areaId, manage: true, ct);
        }
        else
        {
            if (req.FarmingAreaId.HasValue)
                throw AppException.BadRequest("Quy tắc kỹ thuật chỉ có cấu hình hệ thống.");
            if (actor is not null && actor.Role != UserRole.SystemAdministrator)
                throw AppException.Forbidden("Chỉ System Administrator được sửa quy tắc kỹ thuật.");
        }
        var candidates = (await _uow.NotificationRules.FindAsync(r => r.TypeCode == code
            && (r.FarmingAreaId == null || r.FarmingAreaId == req.FarmingAreaId), ct)).ToList();
        var defaults = candidates.FirstOrDefault(r => r.FarmingAreaId == null)
            ?? throw AppException.NotFound("NotificationRule");
        var rule = environmental
            ? candidates.FirstOrDefault(r => r.FarmingAreaId == req.FarmingAreaId)
            : defaults;
        var isNew = rule is null;
        rule ??= new NotificationRule
        {
            FarmingAreaId = req.FarmingAreaId, TypeCode = defaults.TypeCode,
            DisplayName = defaults.DisplayName, IsEnabled = defaults.IsEnabled,
            MinSeverity = defaults.MinSeverity, RecipientRoles = defaults.RecipientRoles,
            ScopeByArea = true, ChannelCodes = defaults.ChannelCodes,
            TitleTemplate = defaults.TitleTemplate, BodyTemplate = defaults.BodyTemplate
        };

        if (req.MinSeverity is not null && !string.IsNullOrWhiteSpace(req.MinSeverity)
            && !Enum.TryParse<AlertSeverity>(req.MinSeverity.Trim(), true, out _))
            throw AppException.BadRequest("minSeverity must be Info, Warning or Critical.");

        if (req.RecipientRoles is not null)
        {
            var roles = SplitCsv(req.RecipientRoles);
            if (roles.Length == 0 || roles.Any(r => !UserRoleParsing.TryParse(r, out _)))
                throw AppException.BadRequest(
                    "recipientRoles must be a comma list of SystemAdministrator, FarmAdministrator, FarmManager, FarmStaff.");
            rule.RecipientRoles = string.Join(',', roles.Select(r => UserRoleParsing.TryParse(r, out var v) ? v.ToString() : r));
        }

        if (req.ChannelCodes is not null)
        {
            var codes = SplitCsv(req.ChannelCodes).Select(c => c.ToLowerInvariant()).ToArray();
            var unknown = codes.Where(c => !KnownChannelCodes.Contains(c)).ToArray();
            if (unknown.Length > 0)
                throw AppException.BadRequest($"Unknown channel(s): {string.Join(", ", unknown)}.");
            rule.ChannelCodes = string.Join(',', codes);
        }

        if (req.IsEnabled is not null) rule.IsEnabled = req.IsEnabled.Value;
        if (environmental) rule.ScopeByArea = true;
        else if (req.ScopeByArea is not null) rule.ScopeByArea = req.ScopeByArea.Value;
        if (req.DisplayName is not null && !string.IsNullOrWhiteSpace(req.DisplayName))
            rule.DisplayName = req.DisplayName.Trim();
        if (req.MinSeverity is not null)
            rule.MinSeverity = string.IsNullOrWhiteSpace(req.MinSeverity) ? null : req.MinSeverity.Trim();
        if (req.TitleTemplate is not null)
            rule.TitleTemplate = string.IsNullOrWhiteSpace(req.TitleTemplate) ? null : req.TitleTemplate.Trim();
        if (req.BodyTemplate is not null)
            rule.BodyTemplate = string.IsNullOrWhiteSpace(req.BodyTemplate) ? null : req.BodyTemplate.Trim();

        if (isNew) await _uow.NotificationRules.AddAsync(rule, ct);
        else _uow.NotificationRules.Update(rule);
        var actorId = _currentUser is { IsAuthenticated: true } ? _currentUser.UserId : (Guid?)null;
        await ConfigAudit.WriteAsync(_uow, actorId, "UpdateNotificationRule", "NotificationRule", rule.Id, $"{rule.TypeCode}|area={rule.FarmingAreaId}", ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<NotificationRuleDto>.Ok(MapRule(rule), "Rule updated.");
    }

    public async Task<ApiResponse<NotificationRulePreviewDto>> PreviewRuleAsync(
        string typeCode, IReadOnlyDictionary<string, string?> tokens, CancellationToken ct = default)
    {
        var code = NormalizeCode(typeCode);
        var rule = (await _uow.NotificationRules.FindAsync(r => r.TypeCode == code && r.FarmingAreaId == null, ct)).FirstOrDefault()
            ?? throw AppException.NotFound("NotificationRule");

        var sample = new Dictionary<string, string?>(tokens, StringComparer.OrdinalIgnoreCase)
        {
            ["message"] = tokens.TryGetValue("message", out var m) && !string.IsNullOrWhiteSpace(m)
                ? m
                : "Nội dung mẫu"
        };
        var values = BuildTokens(code, sample["message"]!, sample.TryGetValue("severity", out var s) ? s : "Warning", sample);
        return ApiResponse<NotificationRulePreviewDto>.Ok(new NotificationRulePreviewDto(
            Render(rule.TitleTemplate ?? "CrabSense Alert", values),
            Render(string.IsNullOrWhiteSpace(rule.BodyTemplate) ? "{message}" : rule.BodyTemplate!, values)));
    }

    public async Task<ApiResponse<IEnumerable<NotificationDeliveryDto>>> GetDeliveriesAsync(
        string? status = null, int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var rows = string.IsNullOrWhiteSpace(status)
            ? await _uow.NotificationDeliveries.GetAllAsync(ct)
            : await _uow.NotificationDeliveries.FindAsync(
                d => d.Status == status.Trim().ToLowerInvariant(), ct);

        return ApiResponse<IEnumerable<NotificationDeliveryDto>>.Ok(
            rows.OrderByDescending(d => d.CreatedAt).Take(take).Select(d => new NotificationDeliveryDto(
                d.Id, d.NotificationId, d.ChannelCode, d.Recipient, d.Status,
                d.SentAt, d.ErrorMessage, d.CreatedAt)));
    }

    public async Task NotifyAsync(
        string typeCode,
        string message,
        Guid? alertId = null,
        Guid? areaId = null,
        string? severity = null,
        IReadOnlyDictionary<string, string?>? tokens = null,
        CancellationToken ct = default)
    {
        await EnsureDefaultChannelsAsync(ct);

        var code = NormalizeCode(typeCode);
        var candidates = (await _uow.NotificationRules.FindAsync(r => r.TypeCode == code
            && (r.FarmingAreaId == null || r.FarmingAreaId == areaId), ct)).ToList();
        var defaults = candidates.FirstOrDefault(r => r.FarmingAreaId == null);
        var rule = areaId.HasValue && NotificationTypeCodes.IsWarningType(code)
            ? candidates.FirstOrDefault(r => r.FarmingAreaId == areaId) ?? defaults
            : defaults;
        var scoped = NotificationTypeCodes.IsWarningType(code)
            || areaId.HasValue || rule?.ScopeByArea == true;

        // Critical environmental warnings are mandatory for the assigned farm.
        // Turning off optional notifications must never suppress them.
        var critical = NotificationTypeCodes.IsWarningType(code)
            && string.Equals(severity, nameof(AlertSeverity.Critical), StringComparison.OrdinalIgnoreCase);
        if (!critical && rule is not null && !rule.IsEnabled) return;
        if (!critical && !SeverityPasses(rule?.MinSeverity, severity)) return;

        var roles = SplitCsv(rule?.RecipientRoles ?? _fallback.FallbackRecipientRoles)
            .Select(r => UserRoleParsing.TryParse(r, out var v) ? (UserRole?)v : null)
            .Where(r => r is not null)
            .Select(r => r!.Value)
            .Distinct()
            .ToArray();
        if (critical) roles = [UserRole.FarmAdministrator, UserRole.FarmManager, UserRole.FarmStaff];
        if (roles.Length == 0) return;

        var recipients = (await _uow.Users.FindAsync(u => u.IsActive && roles.Contains(u.Role), ct)).ToList();
        recipients = await FilterByAreaAsync(recipients, scoped, areaId, ct);

        var values = BuildTokens(code, message, severity, tokens);
        var title = Render(defaults?.TitleTemplate ?? "CrabSense Alert", values);
        var body = Render(
            string.IsNullOrWhiteSpace(defaults?.BodyTemplate) ? "{message}" : defaults!.BodyTemplate!, values);

        var channelCodes = SplitCsv(defaults?.ChannelCodes ?? _fallback.FallbackChannels);
        var channels = (await _uow.NotificationChannels.FindAsync(
            c => c.IsEnabled && channelCodes.Contains(c.ChannelCode), ct)).ToList();
        var pushChannel = channels.FirstOrDefault(c => c.ChannelCode == "push");
        var createInApp = critical || channelCodes.Contains("in_app", StringComparer.OrdinalIgnoreCase);
        var external = scoped ? Array.Empty<NotificationChannel>() : channels.Where(c => c.ChannelCode is "telegram" or "zalo_oa" or "email").ToArray();

        Notification? anchor = null;
        foreach (var user in recipients)
        {
            if (!PrefsAllow(user.NotificationPrefsJson, code, severity)) continue;

            Notification? own = null;
            if (createInApp || pushChannel is not null || (anchor is null && external.Length > 0))
            {
                own = new Notification
                {
                    UserId = user.Id,
                    AlertId = alertId,
                    TypeCode = code,
                    Title = title,
                    Body = body,
                    Channel = createInApp ? "in_app" : "delivery",
                    IsRead = !createInApp
                };
                await _uow.Notifications.AddAsync(own, ct);
                anchor ??= own;
            }

            if (pushChannel is not null)
            {
                var (ok, detail) = await DispatchChannelAsync(pushChannel, title, body, user.Id, ct);
                if (own is not null)
                    await _uow.NotificationDeliveries.AddAsync(new NotificationDelivery
                    {
                        NotificationId = own.Id,
                        ChannelCode = pushChannel.ChannelCode,
                        Recipient = user.Id.ToString(),
                        Status = ok ? "sent" : "failed",
                        ErrorMessage = ok ? null : detail,
                        SentAt = ok ? DateTime.UtcNow : null
                    }, ct);
            }
        }

        // Telegram / Zalo / Email: một đích chung toàn hệ thống (ConfigJson), gắn vào notification đầu tiên.
        if (anchor is not null && !scoped)
        {
            foreach (var ch in external)
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

    // ─── Rule resolution ────────────────────────────────────────────────────

    private static readonly HashSet<string> KnownChannelCodes =
        new(StringComparer.OrdinalIgnoreCase) { "in_app", "push", "telegram", "zalo_oa", "email" };

    private static string NormalizeCode(string? typeCode) =>
        string.IsNullOrWhiteSpace(typeCode) ? NotificationTypeCodes.System : typeCode.Trim().ToLowerInvariant();

    private static string[] SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool SeverityPasses(string? minSeverity, string? severity)
    {
        if (string.IsNullOrWhiteSpace(minSeverity)) return true;
        if (!Enum.TryParse<AlertSeverity>(minSeverity.Trim(), true, out var min)) return true;
        if (!Enum.TryParse<AlertSeverity>(severity ?? "Warning", true, out var actual)) actual = AlertSeverity.Warning;
        return actual >= min;
    }

    private async Task<List<AppUser>> FilterByAreaAsync(
        List<AppUser> users, bool scopeByArea, Guid? areaId, CancellationToken ct)
    {
        if (!scopeByArea) return users;
        if (areaId is not Guid id || id == Guid.Empty)
            return users.Where(user => user.Role == UserRole.SystemAdministrator).ToList();

        var administratorId = await AreaAdministratorQuery.IdAsync(_uow.FarmingAreas.Query(), id, ct);
        var managerIds = (await _uow.FarmManagerAssignments.FindAsync(
            a => a.FarmingAreaId == id, ct)).Select(a => a.ManagerUserId).ToHashSet();
        var staffIds = (await _uow.FarmStaffAssignments.FindAsync(
            a => a.FarmingAreaId == id, ct)).Select(a => a.StaffUserId).ToHashSet();
        return users.Where(user =>
            user.Role switch
            {
                UserRole.FarmAdministrator => user.Id == administratorId,
                UserRole.FarmManager => managerIds.Contains(user.Id),
                UserRole.FarmStaff => staffIds.Contains(user.Id),
                _ => true
            }).ToList();
    }

    private static bool PrefsAllow(string? prefsJson, string typeCode, string? severity)
    {
        if (string.Equals(severity, nameof(AlertSeverity.Critical), StringComparison.OrdinalIgnoreCase))
            return true; // cảnh báo nghiêm trọng không cho tắt
        if (string.IsNullOrWhiteSpace(prefsJson)) return true;

        try
        {
            var prefs = JsonSerializer.Deserialize<NotificationPreferencesDto>(prefsJson, PrefsJsonOpts);
            if (prefs is null) return true;
            return NotificationTypeCodes.IsWarningType(typeCode)
                ? prefs.WarningsEnabled
                : prefs.SystemUpdatesEnabled;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static readonly JsonSerializerOptions PrefsJsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static Dictionary<string, string?> BuildTokens(
        string typeCode, string message, string? severity, IReadOnlyDictionary<string, string?>? extra)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["message"] = message,
            ["type"] = typeCode,
            ["severity"] = severity ?? nameof(AlertSeverity.Warning)
        };
        if (extra is null) return values;
        foreach (var (key, value) in extra)
            if (!string.IsNullOrWhiteSpace(key))
                values[key.Trim()] = value;
        return values;
    }

    /// <summary>Thay {token} — token lạ giữ nguyên để admin thấy lỗi khi preview.</summary>
    private static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        var result = template;
        foreach (var (key, value) in values)
            result = result.Replace("{" + key + "}", value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static NotificationRuleDto MapRule(NotificationRule r) =>
        new(r.Id, r.TypeCode, r.DisplayName, r.IsEnabled, r.MinSeverity, r.RecipientRoles,
            r.FarmingAreaId.HasValue || NotificationTypeCodes.IsWarningType(r.TypeCode) || r.ScopeByArea,
            r.ChannelCodes, r.TitleTemplate, r.BodyTemplate, r.FarmingAreaId);

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
                "email" => (false, "email provider not configured"),
                "in_app" => (true, "in-app"),
                _ => (false, "unsupported notification channel")
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Provider URLs or response bodies may contain credentials.
            _logger.LogWarning("Dispatch {Channel} failed: {ErrorType}", ch.ChannelCode, ex.GetType().Name);
            return (false, "Notification provider request failed.");
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
            return (false, $"Telegram HTTP {(int)res.StatusCode}");
        using var parsed = JsonDocument.Parse(raw);
        return parsed.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
            ? (true, "telegram sent") : (false, "Telegram rejected message");
    }

    private async Task<(bool Ok, string Detail)> SendZaloAsync(
        string? configJson, string title, string body, CancellationToken ct)
    {
        // Zalo OA official send requires access_token + user_id; message template varies by OA setup.
        var (accessToken, userId) = ParseZaloConfig(configJson);
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(userId))
            return (false, "ConfigJson needs access_token + user_id");

        var client = _httpClientFactory.CreateClient("zalo");
        const string url = "https://openapi.zalo.me/v3.0/oa/message/cs";
        var payload = new
        {
            recipient = new { user_id = userId },
            message = new { text = $"{title}\n{body}" }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.TryAddWithoutValidation("access_token", accessToken);
        using var res = await client.SendAsync(request, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return (false, $"Zalo HTTP {(int)res.StatusCode}");
        using var parsed = JsonDocument.Parse(raw);
        return parsed.RootElement.TryGetProperty("error", out var error) && error.TryGetInt32(out var code) && code == 0
            ? (true, "zalo sent") : (false, "Zalo rejected message");
    }

    private async Task<(bool Ok, string Detail)> SendPushAsync(
        Guid userId, string title, string body, CancellationToken ct)
    {
        var tokens = (await _uow.UserPushTokens.FindAsync(
            t => t.UserId == userId && t.IsActive, ct)).ToList();
        if (tokens.Count == 0) return (false, "no registered device tokens");
        if (!_fcm.Enabled || string.IsNullOrWhiteSpace(_fcm.ProjectId)
            || (string.IsNullOrWhiteSpace(_fcm.CredentialsJson) && string.IsNullOrWhiteSpace(_fcm.CredentialsPath)))
            return (false, "FCM HTTP v1 not configured; push was not sent.");
        var project = _fcm.ProjectId.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(project, "^[a-zA-Z0-9-]+$"))
            return (false, "Invalid FCM project ID.");

        var json = !string.IsNullOrWhiteSpace(_fcm.CredentialsJson)
            ? _fcm.CredentialsJson : await File.ReadAllTextAsync(_fcm.CredentialsPath!, ct);
        using var credentialDoc = JsonDocument.Parse(json!);
        if (!credentialDoc.RootElement.TryGetProperty("type", out var type) || type.GetString() != "service_account")
            return (false, "FCM requires service-account credentials.");
        var credential = GoogleCredential.FromJson(json!)
            .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
        var bearer = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(null, ct);
        var client = _httpClientFactory.CreateClient("fcm");
        var url = $"https://fcm.googleapis.com/v1/projects/{project}/messages:send";
        var sent = 0;
        foreach (var token in tokens)
        {
            var payload = new { message = new
            {
                token = token.Token,
                notification = new { title, body },
                data = new { title, body, userId = userId.ToString() }
            } };
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var res = await client.SendAsync(req, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode)
            {
                using var parsed = JsonDocument.Parse(raw);
                if (parsed.RootElement.TryGetProperty("name", out var name) && !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    sent++;
                    token.LastSeenAt = DateTime.UtcNow;
                    _uow.UserPushTokens.Update(token);
                }
            }
            else
            {
                _logger.LogWarning("FCM HTTP {Status} for user {UserId}", (int)res.StatusCode, userId);
                if (res.StatusCode == System.Net.HttpStatusCode.NotFound && raw.Contains("UNREGISTERED", StringComparison.Ordinal))
                {
                    token.IsActive = false;
                    _uow.UserPushTokens.Update(token);
                }
            }
        }
        return sent == tokens.Count ? (true, $"push accepted for {sent} device(s)")
            : (false, $"push accepted for {sent}/{tokens.Count} devices; partial or failed");
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
        new(n.Id, n.UserId, n.AlertId, n.Title, n.Body, n.Channel, n.IsRead, n.CreatedAt, n.TypeCode);

    private static NotificationChannelDto MapChannel(NotificationChannel c) =>
        new(c.Id, c.ChannelCode, c.DisplayName, c.IsEnabled, c.ConfigJson);

    private static PushTokenDto MapPushToken(UserPushToken t) =>
        new(t.Id, t.UserId, t.Token, t.Platform, t.DeviceId, t.IsActive, t.LastSeenAt);
}
