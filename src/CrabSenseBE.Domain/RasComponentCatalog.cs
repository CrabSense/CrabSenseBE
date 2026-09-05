namespace CrabSenseBE.Domain;

/// <summary>Type + mã icon desktop. Thêm loại mới chỉ cần một dòng — không tạo bảng riêng.</summary>
public static class RasComponentCatalog
{
    public const string Culture = "CULTURE";
    public const string Filter = "FILTER";
    public const string DrainTank = "DRAIN_TANK";
    public const string Biofilter = "BIOFILTER";
    public const string Skimmer = "SKIMMER";
    public const string CoralTank = "CORAL_TANK";
    public const string SettlingTank = "SETTLING_TANK";
    public const string Pump = "PUMP";
    public const string Valve = "VALVE";
    public const string Uv = "UV";
    public const string Ozone = "OZONE";
    public const string Heater = "HEATER";

    public static string TypeFromCode(string? code)
    {
        var c = (code ?? "").Trim().ToLowerInvariant();
        return c switch
        {
            "crab_boxes" or "culture" or "boxes" => Culture,
            "drum" or "filter" or "drum_filter" => Filter,
            "discharge_100" or "drain" or "drain_tank" => DrainTank,
            "bio" or "bio_2" or "biofilter" => Biofilter,
            "skimmer" => Skimmer,
            "sand_coral_200" or "coral" or "coral_tank" => CoralTank,
            "settling" or "settling_tank" => SettlingTank,
            "pump" => Pump,
            "valve" => Valve,
            "uv" => Uv,
            "ozone" => Ozone,
            "heater" => Heater,
            _ => Filter
        };
    }

    public static string DefaultIcon(string type, string? code)
    {
        if (!string.IsNullOrWhiteSpace(code)) return code.Trim();
        return type.ToUpperInvariant() switch
        {
            Culture => "crab_boxes",
            Filter => "drum",
            DrainTank => "discharge_100",
            Biofilter => "bio",
            Skimmer => "skimmer",
            CoralTank => "sand_coral_200",
            SettlingTank => "settling",
            Pump => "pump",
            _ => "drum"
        };
    }

    public static IReadOnlyList<(string Code, string Name, string Type)> DefaultPipeline { get; } =
    [
        ("crab_boxes", "Hộp nuôi", Culture),
        ("drum", "Drum Filter", Filter),
        ("discharge_100", "Bể xả", DrainTank),
        ("bio", "Bể vi sinh", Biofilter),
        ("skimmer", "Skimmer", Skimmer),
        ("bio_2", "Bể vi sinh", Biofilter),
        ("sand_coral_200", "Bể san hô", CoralTank),
        ("settling", "Bể lắng", SettlingTank)
    ];
}
