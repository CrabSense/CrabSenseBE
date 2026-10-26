namespace CrabSenseBE.Application.Options;

/// <summary>
/// Ngưỡng phân tích nước (colorimetric) — trước đây hardcode trong service.
/// Giá trị null trong settings = giữ default trong code.
/// </summary>
public class WaterAnalysisOptions
{
    public const string SectionName = "WaterAnalysis";

    public int? StepHoldSeconds { get; set; }

    public List<WaterAnalysisThresholdSetting> Thresholds { get; set; } = new();

    public List<WaterAnalysisAssaySetting> Assays { get; set; } = new();
}

public class WaterAnalysisThresholdSetting
{
    public string Code { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? Unit { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public decimal? WarningMin { get; set; }
    public decimal? WarningMax { get; set; }
    public string? Display { get; set; }
}

public class WaterAnalysisAssaySetting
{
    public string Analyte { get; set; } = string.Empty;
    public string? Label { get; set; }
    public bool? Configured { get; set; }
    public decimal? SampleVolumeMl { get; set; }
    public int? ReactionTimeSeconds { get; set; }
    public string? Reagent1Dose { get; set; }
    public string? Reagent2Dose { get; set; }
    public string? AiModelId { get; set; }
    public decimal? AiConfidenceMin { get; set; }
}

/// <summary>Fallback khi DB chưa có NotificationRule tương ứng.</summary>
public class NotificationOptions
{
    public const string SectionName = "Notifications";

    public string FallbackRecipientRoles { get; set; } =
        "FarmAdministrator,FarmManager,FarmStaff";

    public string FallbackChannels { get; set; } =
        "in_app,push,telegram,zalo_oa,email";
}
