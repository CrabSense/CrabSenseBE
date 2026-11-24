using System.Globalization;

namespace CrabSenseBE.Application.Common;

/// <summary>Canonical folders for new media. Existing storage keys remain unchanged.</summary>
public static class MediaStorageLayout
{
    public const string Inbound = "01_NhapHang";
    public const string Farming = "02_NuoiCua";
    public const string Recording = "03_GhiHinh";
    public const string Water = "04_PhanTichNuoc";
    public const string Harvest = "05_ThuHoach";
    public const string Frozen = "06_KhoDongLanh";
    public const string Equipment = "07_ThietBi";
    public const string Users = "08_NguoiDung";
    public const string Ai = "09_AI";
    public const string SystemFiles = "10_HeThong";
    public const string Pending = "99_TamThoi";

    private static readonly HashSet<string> Roots = new(StringComparer.Ordinal)
    {
        Inbound, Farming, Recording, Water, Harvest, Frozen, Equipment,
        Users, Ai, SystemFiles, Pending
    };

    public static string Segment(string? value)
    {
        var safe = MediaFolderPath.SanitizeSegment(value ?? "");
        return safe is "." or ".." or "Other" ? "_pending" : safe;
    }

    private static string DatePath(DateTime date) =>
        date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    public static string CameraRecording(string? area, string? row, Guid deviceId,
        string? deviceCode, DateTime startedAtUtc, string? mode)
    {
        var source = mode?.Trim().ToLowerInvariant() switch
        {
            "manual" => "ThuCong",
            "event" => "SuKien",
            _ => "DinhKy"
        };
        return $"{Recording}/{Segment(area)}/{Segment(row)}/{Segment(string.IsNullOrWhiteSpace(deviceCode) ? deviceId.ToString("D") : deviceCode)}/{DatePath(startedAtUtc)}/{source}";
    }

    public static string WaterRun(string? area, Guid runId, Guid? batchId,
        string? analyte, DateTime startedAtUtc)
    {
        var chemical = analyte?.Trim().ToUpperInvariant() switch
        {
            "NO2" => "NO2", "NO3" => "NO3", "NH3" => "NH3", "PH" => "PH",
            _ => "_pending"
        };
        return $"{Water}/{Segment(area)}/{DatePath(startedAtUtc)}/{(batchId ?? runId):D}/{chemical}/Raw";
    }

    /// <summary>Translate legacy folder contracts without changing client APIs.</summary>
    public static string Canonicalize(string? folder)
    {
        var parts = (folder ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Segment).ToArray();
        if (parts.Length == 0) return $"{Pending}/image";
        if (Roots.Contains(parts[0])) return string.Join("/", parts);

        string Tail(int index) => string.Join("/", parts.Skip(index));
        string Under(string root, string tail) => string.IsNullOrEmpty(tail) ? root : $"{root}/{tail}";
        switch (parts[0].ToLowerInvariant())
        {
            case "nhaphang": return Under(Inbound, Tail(1));
            case "donglanh": return Under(Frozen, Tail(1));
            case "thuhoach": return Under(Harvest, Tail(1));
            case "thietbi": return Under(Equipment, Tail(1));
            case "users": return Under(Users, Tail(1));
            case "nuoc": return Under(Water, Tail(1));
            case "ai": return Under(Ai, Tail(1));
            case "logs": case "log": return Under($"{SystemFiles}/Logs", Tail(1));
            case "video": return Under($"{Recording}/_pending", Tail(1));
            case "image": return Under($"{Pending}/image", Tail(1));
            case "media":
                return parts.Length > 1 ? Canonicalize(Tail(1)) : $"{Pending}/image";
        }
        var marker = Array.FindIndex(parts, 1, p => p is "ThietBi" or "ThuHoach" or "Nuoc");
        if (marker >= 0)
        {
            var root = parts[marker] switch
            {
                "ThietBi" => Equipment, "ThuHoach" => Harvest, _ => Water
            };
            return Under(root, string.Join("/", parts.Where((_, index) => index != marker)));
        }
        return Under(Farming, Tail(0));
    }
}
