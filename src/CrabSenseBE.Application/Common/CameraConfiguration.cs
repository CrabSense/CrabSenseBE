using System.Globalization;
using System.Net;
using System.Text.Json;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Application.Common;

/// <summary>Camera metadata only. A configured URL or TCP port is not evidence of live video.</summary>
public static class CameraConfiguration
{
    public static bool IsCamera(string? type, string? stream, string? snapshot) =>
        (type ?? "").Contains("cam", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrWhiteSpace(stream) || !string.IsNullOrWhiteSpace(snapshot);

    public static bool IsCamera(Device device) => IsCamera(device.DeviceType, device.StreamUrl, device.SnapshotUrl);

    public static string? SafeUrl(string? value, bool snapshot = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.Scheme != "http" && uri.Scheme != "https" && (snapshot || uri.Scheme != "rtsp" && uri.Scheme != "rtsps")))
            return null;
        // Never expose credentials or signed access tokens in device metadata.
        if (!string.IsNullOrEmpty(uri.Query)) return null;
        return value.Trim();
    }

    public static void ValidateUrl(string? value, bool snapshot = false)
    {
        if (!string.IsNullOrWhiteSpace(value) && SafeUrl(value, snapshot) is null)
            throw AppException.BadRequest("URL Camera phải là HTTP(S) hoặc RTSP(S) hợp lệ (ảnh chỉ HTTP(S)), không chứa tài khoản, query hoặc fragment. Dùng Camera Agent cho nguồn cần xác thực.");
    }

    public static void ValidateMetadata(string? ip, string? mac, string? resolution)
    {
        if (ip?.Length > 64 || mac?.Length > 64 || resolution?.Length > 32)
            throw AppException.BadRequest("IP/MAC tối đa 64 ký tự; độ phân giải tối đa 32 ký tự.");
        if (!string.IsNullOrWhiteSpace(ip) && !IPAddress.TryParse(ip.Trim(), out _))
            throw AppException.BadRequest("IpAddress phải là địa chỉ IPv4 hoặc IPv6; cổng đặt trong StreamUrl.");
    }

    public static string Status(Device device, DateTime now)
    {
        if (device.Status == DeviceStatus.Maintenance) return "Maintenance";
        if (!device.LastSeenAt.HasValue || device.LastSeenAt.Value < now.AddMinutes(-15))
            return "Offline";
        return device.Status.ToString();
    }

    public static void ValidateSchedule(string? json, bool required)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            if (required) throw AppException.BadRequest("Scheduled cần lịch ghi hình.");
            return;
        }
        if (json.Length > 16000) throw AppException.BadRequest("Lịch ghi vượt quá giới hạn.");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("timezone", out var zone) || zone.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array
                || slots.GetArrayLength() == 0 || slots.GetArrayLength() > 168)
                throw AppException.BadRequest("Lịch phải gồm timezone IANA và slots (day, start, end).");
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone.GetString()!); }
            catch (TimeZoneNotFoundException) { throw AppException.BadRequest("Timezone không hợp lệ."); }
            catch (InvalidTimeZoneException) { throw AppException.BadRequest("Timezone không hợp lệ."); }
            var days = new Dictionary<int, List<(TimeOnly Start, TimeOnly End)>>();
            foreach (var slot in slots.EnumerateArray())
            {
                if (slot.ValueKind != JsonValueKind.Object
                    || !slot.TryGetProperty("day", out var day) || day.ValueKind != JsonValueKind.Number || !day.TryGetInt32(out var d) || d < 1 || d > 7
                    || !slot.TryGetProperty("start", out var start) || start.ValueKind != JsonValueKind.String
                    || !slot.TryGetProperty("end", out var end) || end.ValueKind != JsonValueKind.String
                    || !TimeOnly.TryParseExact(start.GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var a)
                    || !TimeOnly.TryParseExact(end.GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var b)
                    || a >= b)
                    throw AppException.BadRequest("Slot cần day 1–7 (thứ Hai–Chủ nhật), start/end HH:mm, start < end.");
                if (!days.TryGetValue(d, out var list)) days[d] = list = [];
                if (list.Any(x => a < x.End && b > x.Start))
                    throw AppException.BadRequest("Các slot ghi hình không được chồng nhau.");
                list.Add((a, b));
            }
        }
        catch (JsonException) { throw AppException.BadRequest("WeeklyScheduleJson không hợp lệ."); }
    }
}
