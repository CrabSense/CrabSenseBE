using System.Text.Json;

namespace CrabSenseBE.Application.Common;

/// <summary>Serialize / parse JSON string arrays stored on entities (e.g. crab image URLs).</summary>
public static class JsonStringList
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, Opts) ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? urls, int maxCount = 20)
    {
        if (urls is null) return Array.Empty<string>();
        return urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();
    }

    public static string Serialize(IEnumerable<string>? urls, int maxCount = 20)
        => JsonSerializer.Serialize(Normalize(urls, maxCount), Opts);

    public static IReadOnlyList<string> Merge(string? existingJson, IEnumerable<string>? extra, int maxCount = 20)
    {
        var merged = Parse(existingJson).Concat(extra ?? Array.Empty<string>());
        return Normalize(merged, maxCount);
    }
}
