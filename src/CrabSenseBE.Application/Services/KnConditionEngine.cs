using CrabSenseBE.Application.DTOs.Condition;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Pure Kn engine — port of crab_condition/crab_condition.py + KN_METHOD.md.
/// Stage-1 calibration from RAS HCM (n=9); replace coefficients when n≥100.
/// </summary>
public static class KnConditionEngine
{
    // ras_hcm_raw: W = a * CW^b  (CW in cm, W in g) — crab_condition.py
    public const double CwA = 0.81884661;
    public const double CwB = 2.6882;

    // KN_METHOD yếm vuông CL: W = a * CL^b  (CL in cm)
    public const double ClA = 0.1664;
    public const double ClB = 2.9641;

    // DWG g/day — growth_module.py / Suprapto 2014
    private static readonly Dictionary<string, double> Dwg = new(StringComparer.OrdinalIgnoreCase)
    {
        ["duc"] = 1.07,
        ["cai"] = 0.80,
        ["yem_vuong"] = 0.80,
        ["unknown"] = 0.90
    };

    public static CrabConditionDto Evaluate(EvaluateCrabConditionRequest req)
    {
        if (req.WeightG <= 0)
            throw new ArgumentException("WeightG must be > 0.");

        var type = NormalizeType(req.CrabType);
        var (west, dim, profile) = EstimateWeight(
            req.CarapaceWidthCm, req.CarapaceLengthCm, req.Dimension);

        var kn = (double)req.WeightG / west;
        var (status, recommendation, alert) = Classify(kn);
        var meat = EstimateMeat((double)req.WeightG);

        return new CrabConditionDto(
            Id: null,
            BoxId: req.BoxId,
            BoxCode: req.BoxCode,
            CrabId: req.CrabId,
            WeightObservedG: req.WeightG,
            CarapaceWidthCm: req.CarapaceWidthCm,
            CarapaceLengthCm: req.CarapaceLengthCm,
            WeightEstimatedG: Math.Round((decimal)west, 1),
            Kn: Math.Round((decimal)kn, 4),
            Status: status,
            Recommendation: recommendation,
            AlertHarvest: alert,
            ProfileUsed: profile,
            DimensionUsed: dim,
            MeatEstimatedG: Math.Round((decimal)meat, 1),
            MoltingStatusHint: ToMoltingHint(status),
            RecordedAt: null);
    }

    public static (double West, string Dimension, string Profile) EstimateWeight(
        decimal? cwCm, decimal? clCm, string dimension)
    {
        var dim = (dimension ?? "auto").Trim().ToLowerInvariant();
        if (dim is "cw" or "width")
        {
            if (cwCm is null or <= 0) throw new ArgumentException("CarapaceWidthCm is required for dimension=cw.");
            return (CwA * Math.Pow((double)cwCm.Value, CwB), "cw", "ras_hcm_raw");
        }

        if (dim is "cl" or "length")
        {
            if (clCm is null or <= 0) throw new ArgumentException("CarapaceLengthCm is required for dimension=cl.");
            return (ClA * Math.Pow((double)clCm.Value, ClB), "cl", "kn_method_cl");
        }

        // auto: prefer CW (matches quick_eval API in Python), else CL
        if (cwCm is > 0)
            return (CwA * Math.Pow((double)cwCm.Value, CwB), "cw", "ras_hcm_raw");
        if (clCm is > 0)
            return (ClA * Math.Pow((double)clCm.Value, ClB), "cl", "kn_method_cl");

        throw new ArgumentException("Provide CarapaceWidthCm and/or CarapaceLengthCm (> 0).");
    }

    public static (string Status, string Recommendation, bool AlertHarvest) Classify(double kn)
    {
        if (kn >= 1.20)
            return ("pre_molt",
                $"Kn={kn:F3} — CUA SẮP LỘT. Thu hoạch trong 6–12h hoặc tách hộp theo dõi.",
                true);
        if (kn >= 1.05)
            return ("full_meat",
                $"Kn={kn:F3} — Đầy thịt, chất lượng thương phẩm tốt. Có thể thu hoạch.",
                false);
        if (kn >= 0.88)
            return ("normal",
                $"Kn={kn:F3} — Bình thường. Tiếp tục theo dõi.",
                false);
        if (kn >= 0.75)
            return ("lean",
                $"Kn={kn:F3} — Ốp nhẹ. Kiểm tra khẩu phần ăn và chất lượng nước.",
                false);
        return ("post_molt",
            $"Kn={kn:F3} — Mới lột / vỏ mềm. Chờ cứng vỏ ~7–14 ngày.",
            false);
    }

    /// <summary>Ali et al. meat yield approx: Mwt ≈ -0.34 + 0.388×W</summary>
    public static double EstimateMeat(double weightG) => Math.Max(0, -0.34 + 0.388 * weightG);

    public static DailyCheckDto DailyCheck(DailyCheckRequest req)
    {
        var type = NormalizeType(req.CrabType);
        decimal? kn = null;
        string? status = null;
        var alert = false;
        var recommendation = "Thiếu cân nặng + kích thước mai để tính Kn.";

        if (req.WeightG is > 0 && (req.CarapaceWidthCm is > 0 || req.CarapaceLengthCm is > 0))
        {
            var eval = Evaluate(new EvaluateCrabConditionRequest(
                req.WeightG.Value, req.CarapaceWidthCm, req.CarapaceLengthCm, type,
                BoxId: req.BoxId, CrabId: req.CrabId));
            kn = eval.Kn;
            status = eval.Status;
            alert = eval.AlertHarvest;
            recommendation = eval.Recommendation;
        }

        double? wPred = null;
        if (req.WeightInitialG is > 0 && req.DaysInCulture >= 0)
        {
            var dwg = Dwg.GetValueOrDefault(type, 0.90);
            wPred = (double)req.WeightInitialG.Value + dwg * req.DaysInCulture;
        }

        // Pre-molt composite signal (growth_module.phat_hien_lot)
        if (req.WeightG is > 0 && wPred is > 0 && kn is >= 1.15m)
        {
            var ratio = (double)req.WeightG.Value / wPred.Value;
            if (ratio >= 1.10 && !alert)
            {
                alert = true;
                status ??= "pre_molt";
                recommendation = $"W thực vượt DWG {(ratio - 1) * 100:F0}% và Kn≥1.15 — sắp lột, thu 6–12h.";
            }
        }

        if (req.CarapaceLengthCm is > 0 && req.CarapaceLengthInitialCm is > 0 && kn is < 1.0m)
        {
            var clRatio = (double)req.CarapaceLengthCm.Value / (double)req.CarapaceLengthInitialCm.Value;
            if (clRatio >= 1.15)
            {
                status = "post_molt";
                recommendation = $"CL tăng {(clRatio - 1) * 100:F0}% nhưng Kn<1 — vừa lột, chờ cứng vỏ.";
            }
        }

        if (req.WaterTempC is { } t && (t < 26 || t > 32))
            recommendation += $" Nhiệt độ {t:F1}°C ngoài vùng tối ưu ~29°C.";

        var feedBase = (double)(req.WeightG ?? req.WeightInitialG ?? 0);
        var feedTomorrow = feedBase > 0 ? feedBase * 0.05 : (double?)null; // 5% BW

        return new DailyCheckDto(
            Kn: kn,
            Status: status,
            AlertHarvest: alert,
            Recommendation: recommendation,
            WeightPredictedDwg: wPred is null ? null : Math.Round((decimal)wPred.Value, 1),
            FeedTomorrowG: feedTomorrow is null ? null : Math.Round((decimal)feedTomorrow.Value, 1),
            FeedTime: "22:00",
            FeedType: "cá tạp");
    }

    public static string NormalizeType(string? crabType)
    {
        var t = (crabType ?? "unknown").Trim().ToLowerInvariant();
        return t switch
        {
            "male" or "đực" or "duc" => "duc",
            "female" or "cái" or "cai" => "cai",
            "gravid" or "yemvuong" or "yem_vuong" or "yếm vuông" => "yem_vuong",
            _ => "unknown"
        };
    }

    public static string ToMoltingHint(string status) => status switch
    {
        "pre_molt" => "preMolt",
        "post_molt" => "postMolt",
        _ => "hardShell"
    };
}
