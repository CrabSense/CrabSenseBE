namespace CrabSenseBE.Application.Common;

/// <summary>
/// Cây Drive (gốc folder My Drive CrabSense):
///   {khu}/{dãy}/{hộp}/{cua}
///   {khu}/_khu — ảnh khu
///   NhapHang/{mã lô} — phiếu nhập, cua chưa thả
/// </summary>
public static class MediaFolderPath
{
    public const string PendingSegment = "_pending";
    public const string AreasLeaf = "_khu";
    public const string InboundRoot = "NhapHang";
    public const string FrozenRoot = "DongLanh";
    public const string HarvestFolder = "ThuHoach";
    public const string OpsFolder = "NhatKy";
    public const string DeviceFolder = "ThietBi";
    public const string WaterFolder = "Nuoc";
    public const string UsersRoot = "Users";
    public const string MoltFolder = "LotXac";
    public const string FeedingFolder = "ChoAn";

    public static string Join(params string?[] segments)
    {
        var parts = new List<string>();
        foreach (var raw in segments)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            foreach (var piece in raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var safe = SanitizeSegment(piece);
                if (!string.Equals(safe, "Other", StringComparison.Ordinal))
                    parts.Add(safe);
            }
        }
        return parts.Count == 0 ? PendingSegment : string.Join('/', parts);
    }

    public static string Build(string? entityType, Guid? entityId, string? category = null, string? folderName = null)
    {
        var table = MapTable(entityType, category);
        if (table.StartsWith("Media/", StringComparison.Ordinal))
            return table;

        var leaf = !string.IsNullOrWhiteSpace(folderName)
            ? SanitizeSegment(folderName)
            : entityId is Guid g && g != Guid.Empty
                ? g.ToString("D")
                : PendingSegment;
        return Join(table, leaf);
    }

    public static string MapTable(string? entityType, string? category = null)
    {
        var raw = (entityType ?? category ?? "image").Trim();
        if (raw.Length == 0) raw = "image";

        var key = Normalize(raw);
        return key switch
        {
            "crab" or "crabs" => "Crabs",
            "crabfeeding" or "crabfeedings" or "feedingphoto" or "feedingphotos" => "CrabFeedings",
            "crablot" or "crablots" or "lot" or "lots" or "inbound" => InboundRoot,
            "farmingarea" or "farmingareas" or "farm" or "farms" or "area" or "areas" => "FarmingAreas",
            "farmingrow" or "farmingrows" or "row" or "rows" => "FarmingRows",
            "box" or "boxes" => "Boxes",
            "farmoperation" or "farmoperations" or "operation" or "operations" => OpsFolder,
            "harvestvoucher" or "harvestvouchers" or "harvest" or "harvests" => HarvestFolder,
            "harvestline" or "harvestlines" => HarvestFolder,
            "frozenlot" or "frozenlots" or "frozen" => FrozenRoot,
            "inspection" or "inspections" => "Inspections",
            "wateranalysis" or "wateranalyses" => WaterFolder,
            "user" or "users" or "appuser" => UsersRoot,
            "device" or "devices" => DeviceFolder,
            "alert" or "alerts" => "Alerts",
            "video" or "videos" => "Media/video",
            "log" or "logs" => "Media/log",
            "image" or "images" or "photo" or "photos" or "media" => "Media/image",
            _ => SanitizeSegment(raw)
        };
    }

    public static string Leaf(Guid? id, string? code)
        => !string.IsNullOrWhiteSpace(code)
            ? SanitizeSegment(code)
            : id is Guid g && g != Guid.Empty
                ? g.ToString("D")
                : PendingSegment;

    private static string Normalize(string raw)
    {
        var chars = raw.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars).ToLowerInvariant();
    }

    public static string SanitizeSegment(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Replace('/', '_').Replace('\\', '_').Replace("'", "_");
        var trimmed = name.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "Other" : trimmed;
    }
}
