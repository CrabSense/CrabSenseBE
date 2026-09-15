using CrabSenseBE.Application.DTOs.Water;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Bộ tính liều khoáng Ca/Mg cho nước RAS (Scylla paramamosain).
/// Hàm thuần — không DI, không DB, test trực tiếp như <see cref="KnConditionEngine"/>.
///
/// MỤC TIÊU THEO ĐỘ MẶN (không dùng một công thức tuyến tính cho mọi độ mặn):
///
///   · &lt; 3‰ → Ca 150 / Mg 450 mg/L (tổng 600) — ĐÃ KIỂM CHỨNG.
///     Qin et al. 2026, Aquaculture Reports 49:103696 (DOI 10.1016/j.aqrep.2026.103696):
///     ở 1,5‰, "150 mg/L Ca2+ and 450 mg/L Mg2+" là tối ưu; tổng 600 mg/L cho SOD/CAT/AKP cao
///     nhất và MDA thấp nhất; tỷ lệ 1:2 và 1:3 trội hơn 1:1 và 1:4. Bài này cũng chốt rằng cùng
///     450 mg/L Ca tốt ở độ mặn nước biển nhưng ỨC CHẾ ở 1,5‰ ⇒ độ mặn là điều kiện tiên quyết.
///
///   · 3–12‰ → Ca 300 / Mg 900 mg/L (tổng 1200) — CHƯA XÁC MINH ĐƯỢC trong mã nguồn này.
///     Cơ sở là Li et al. 2025 (Aquaculture 586:741875), trong đó 300:300 / 300:900 / 400:1200
///     mô phỏng ba vùng nuôi thành công — không phải một bảng xếp hạng. Bản corrigendum
///     (Aquaculture 598:741946) chỉ sửa hình và KHÔNG chứa số liệu kết quả, nên dãy độ mặn và
///     thứ hạng các mức CHƯA được xác minh. Chính bài 2026 khuyến nghị "adjust ... based on the
///     local basic Ca2+ and Mg2+ contents", tức đây là mục tiêu tham chiếu, không bắt buộc.
///
///   · &gt; 12‰ → Ca 400 / Mg 1200 mg/L (tổng 1600) — NGOẠI SUY, luôn kèm cảnh báo.
///
/// Ba nguyên tắc bắt buộc:
///   1. Độ mặn CHỈ chọn mục tiêu và so sánh trạng thái — KHÔNG vào phép tính gram CaCl2/MgCl2.
///   2. KHÔNG suy Mg từ Ca (Mg = Ca×3 là sai về đo lường; tỷ lệ 1:3 là điều kiện thí nghiệm).
///      Không đo Mg ⇒ không tính được liều MgCl2, và phải nói rõ cho người dùng.
///   3. Không cộng Ca/Mg vào phép tính tăng độ mặn — cần biết nguồn nước/muối và thành phần ion.
///
/// CẢNH BÁO TỔNG LIỀU: tổng Ca+Mg quan trọng ngang tỷ lệ. Ở 1,5‰, tổng 900 mg/L với tỷ lệ 1:4
/// làm chết toàn bộ (0% sống) và 300 mg/L với 1:1 chỉ còn 12±2%; bài 2026 kết luận "excessive Ca
/// and Mg can cause difficulty in molting". RAS tích tụ Ca/Mg (không mất theo N/P như đạm/lân),
/// nên tổng mục tiêu cao luôn kèm cảnh báo chia liều — xem <see cref="HighTotalWarningMgL"/>.
/// </summary>
public static class MineralDosingCalculator
{
    // ── Hệ số quy đổi sản phẩm thương mại ────────────────────────────────────
    // g sản phẩm cho 1 mg/L nguyên tố trong 1000 L = 1000 × phần khối lượng nguyên tố.
    //
    // CaCl2·2H2O 96%:  M(Ca)=40,078 / M(CaCl2·2H2O)=147,01 = 27,26% nguyên chất
    //                  × 0,96 độ tinh khiết = 26,17% ⇒ 1000 × 0,2617 ≈ 261,7
    // MgCl2·6H2O 98,5%: M(Mg)=24,305 / M(MgCl2·6H2O)=203,30 = 11,96%
    //                  × 0,985 = 11,78% ⇒ 1000 × 0,1178 ≈ 117,8
    public const double CaCl2Divisor = 261.7;
    public const double MgCl2Divisor = 117.8;

    public const string CaCl2Product = "CaCl2·2H2O 96%";
    public const string MgCl2Product = "MgCl2·6H2O 98,5%";

    /// <summary>
    /// Độ mặn (‰) tối đa đã được kiểm chứng trực tiếp. Trên mức này là ngoại suy.
    /// (Li et al. 2025 phủ 3–12‰ nhưng số liệu chưa xác minh được trong repo này.)
    /// </summary>
    public const decimal ResearchMaxSalinityPpt = 12m;

    /// <summary>Độ mặn (‰) tối đa dùng band 300/900. Trên mức này chuyển sang 400/1200.</summary>
    public const decimal HighSalinityBandPpt = 12m;

    /// <summary>Độ mặn (‰) dưới mức này dùng band nước cực thấp 150/450.</summary>
    public const decimal LowSalinityBandPpt = 3m;

    /// <summary>
    /// Tổng Ca+Mg (mg/L) đã kiểm chứng là tối ưu: 600 mg/L ở 1,5‰ (150+450).
    /// Qin et al. 2026, Aquaculture Reports 49:103696.
    /// </summary>
    public const decimal ValidatedOptimalTotalMgL = 600m;

    /// <summary>
    /// Tổng Ca+Mg (mg/L) từ mức này trở lên phải cảnh báo vì vượt xa vùng đã kiểm chứng.
    /// Bằng chứng ở 1,5‰: tổng 900 mg/L với tỷ lệ 1:4 làm chết toàn bộ (0% sống), tổng 300 mg/L
    /// với 1:1 chỉ còn 12±2%, và "excessive Ca and Mg can cause difficulty in molting".
    /// RAS tích tụ Ca/Mg nên tổng cao là rủi ro thật, không chỉ trên lý thuyết.
    /// </summary>
    public const decimal HighTotalWarningMgL = 1200m;

    /// <summary>
    /// Trần tăng nồng độ Ca/Mg MỖI LẦN châm (mg/L) — mặc định 50.
    ///
    /// ĐÂY LÀ NGƯỠNG AN TOÀN VẬN HÀNH, KHÔNG PHẢI SỐ LIỆU NGHIÊN CỨU.
    /// Qin et al. 2026 và Li et al. 2025 đều châm/thử ở mức nồng độ đích rồi đo kết quả;
    /// không bài nào thử ảnh hưởng của TỐC ĐỘ tăng Ca/Mg trên Scylla paramamosain. Nên
    /// không có con số "mg/L mỗi ngày" nào được kiểm chứng để trích dẫn.
    ///
    /// 50 mg/L được chọn vì hai lý do vận hành, không phải vì sinh học:
    ///   1. Đủ nhỏ so với tổng đã kiểm chứng (600 mg/L ở 1,5‰) để không nhảy cóc qua vùng
    ///      có thể gây sốc thẩm thấu.
    ///   2. Đủ lớn để giữ số lần châm trong khoảng làm được thật (xem <see cref="ComfortableDoseCount"/>).
    /// Người dùng giảm xuống nữa thì an toàn hơn, chỉ tốn thời gian.
    /// </summary>
    public const decimal DefaultMaxIncreasePerDoseMgL = 50m;

    /// <summary>
    /// Số lần châm còn thực tế với một chương trình. Vượt mức này thì nên kéo dài
    /// (nhiều tuần) hoặc xem lại mục tiêu, chứ không phải châm dồn cho đủ số lần.
    /// </summary>
    public const int ComfortableDoseCount = 7;

    /// <summary>
    /// Số giờ tối thiểu giữa hai lần châm — thực chất là "1 lần/ngày".
    /// Giãn cách cần thiết để đo lại Ca/Mg trước khi quyết định lần kế tiếp.
    /// </summary>
    public const int DefaultDoseIntervalHours = 24;

    public const string ModeAuto = "auto";
    public const string ModeManual = "manual";

    public const string SalinityOk = "SALINITY_OK";
    public const string SalinityLow = "SALINITY_LOW";
    public const string SalinityHigh = "SALINITY_HIGH";
    public const string SalinityUnknown = "SALINITY_UNKNOWN";

    /// <summary>
    /// ĐÃ biết độ mặn hiện tại nhưng người dùng không nhập mức mong muốn ⇒ không có gì để
    /// so sánh. Phải phân biệt với <see cref="SalinityUnknown"/>: nhập 20‰ rồi bảo
    /// "chưa đủ dữ liệu độ mặn" là sai và làm nông dân tưởng ô nhập bị lỗi.
    /// </summary>
    public const string SalinityNoTarget = "SALINITY_NO_TARGET";

    public const string SourceMeasured = "measured";
    public const string SourceEstimated = "estimated";
    public const string SourceMissing = "missing";

    /// <summary>
    /// Đề xuất mục tiêu Ca/Mg theo độ mặn. Đây là khuyến nghị, KHÔNG phải giá trị bắt buộc.
    /// Trả (null, null) khi chưa biết độ mặn.
    /// </summary>
    public static (decimal? Ca, decimal? Mg) RecommendTargets(decimal? salinityPpt)
    {
        if (salinityPpt is null || salinityPpt < 0) return (null, null);

        if (salinityPpt < LowSalinityBandPpt) return (150m, 450m);   // nước cực thấp
        if (salinityPpt <= HighSalinityBandPpt) return (300m, 900m); // 3–12‰
        return (400m, 1200m);                                        // > 12‰
    }

    /// <summary>Đề xuất mục tiêu kèm ghi chú cho UI — dùng cho màn nhập liệu khi chưa có mục tiêu.</summary>
    public static MineralTargetRecommendationDto Recommend(decimal? salinityPpt)
    {
        if (salinityPpt is < 0)
            throw new ArgumentException("SalinityCurrentPpt must be >= 0.");

        var (ca, mg) = RecommendTargets(salinityPpt);
        var warnings = SalinityBandWarnings(salinityPpt);

        if (ca is null)
        {
            warnings.Add("Chưa có độ mặn nên không đề xuất được mục tiêu Ca/Mg — hãy nhập độ mặn hoặc tự nhập mục tiêu.");
            return new MineralTargetRecommendationDto(
                salinityPpt, null, null, null, null,
                "Chưa đủ dữ liệu để đề xuất.", warnings);
        }

        var total = ca + mg;
        var highTotal = HighTotalWarning(total);
        if (highTotal is not null) warnings.Add(highTotal);

        return new MineralTargetRecommendationDto(
            salinityPpt, ca, mg, RatioString(ca, mg), total,
            "Đây là mục tiêu ĐỀ XUẤT theo độ mặn, không phải giá trị bắt buộc.",
            warnings);
    }

    /// <summary>Tính liều CaCl2/MgCl2 từ chênh lệch hiện tại → mục tiêu.</summary>
    public static MineralDoseResult Calculate(MineralDoseRequest req)
    {
        Validate(req);

        var mode = NormalizeMode(req.TargetMode);

        // Chỉ lấy cảnh báo về ĐỘ MẶN/BAND, KHÔNG lấy cảnh báo tổng của đề xuất:
        // ở chế độ MANUAL người dùng có thể đặt mục tiêu khác hẳn, nên cảnh báo tổng
        // phải tính trên mục tiêu THỰC SỰ đem đi châm (xem totalTarget ở dưới).
        var warnings = SalinityBandWarnings(req.SalinityCurrentPpt);

        var rec = Recommend(req.SalinityCurrentPpt);
        var (recCa, recMg) = (rec.RecommendedCalciumMgL, rec.RecommendedMagnesiumMgL);

        // ── Mục tiêu hiệu dụng ───────────────────────────────────────────────
        // auto   : mục tiêu nào người dùng bỏ trống thì lấy đề xuất
        // manual : chỉ dùng đúng mục tiêu người dùng nhập, không tự sửa
        decimal? caTarget = req.CalciumTargetMgL;
        decimal? mgTarget = req.MagnesiumTargetMgL;
        var usedRecommended = false;

        if (mode == ModeAuto)
        {
            if (caTarget is null && recCa is not null) { caTarget = recCa; usedRecommended = true; }
            if (mgTarget is null && recMg is not null) { mgTarget = recMg; usedRecommended = true; }

            if (req.SalinityCurrentPpt is null)
                warnings.Add("Chưa nhập độ mặn nên không đề xuất được mục tiêu — cần nhập mục tiêu Ca/Mg thủ công.");
        }
        else
        {
            if (caTarget is null)
                warnings.Add("Chế độ MANUAL nhưng chưa nhập Ca mục tiêu — không tính được liều CaCl2.");
            if (mgTarget is null)
                warnings.Add("Chế độ MANUAL nhưng chưa nhập Mg mục tiêu — không tính được liều MgCl2.");
        }

        // ── Ca hiện tại → chênh lệch → liều CaCl2 ─────────────────────────────
        var caSource = SourceOf(req.CalciumCurrentMgL, req.CalciumMeasured);
        var (caDeficit, caDose) = DeficitAndDose(
            req.CalciumCurrentMgL, caTarget, req.WaterVolumeL, CaCl2Divisor);

        if (req.CalciumCurrentMgL is null)
        {
            if (caTarget is not null)
                warnings.Add("Chưa đo Ca hiện tại — cần test Ca mới tính được liều CaCl2.");
        }
        else if (!req.CalciumMeasured)
        {
            warnings.Add("Ca hiện tại là số ƯỚC LƯỢNG, không phải kết quả test — liều CaCl2 chỉ mang tính tham khảo.");
        }

        if (caDeficit is < 0)
            warnings.Add($"Ca đang VƯỢT mục tiêu {Math.Abs(caDeficit.Value):0.##} mg/L — không châm, kiểm tra lại nguồn nước.");

        // ── Mg hiện tại → chênh lệch → liều MgCl2 ─────────────────────────────
        var mgSource = SourceOf(req.MagnesiumCurrentMgL, req.MagnesiumMeasured);
        var (mgDeficit, mgDose) = DeficitAndDose(
            req.MagnesiumCurrentMgL, mgTarget, req.WaterVolumeL, MgCl2Divisor);

        if (req.MagnesiumCurrentMgL is null)
        {
            // Điểm cốt lõi: KHÔNG suy Mg từ Ca. Thiếu Mg ⇒ nói rõ chứ không đoán.
            if (mgTarget is not null)
                warnings.Add("Chưa đo Mg — KHÔNG tính được liều MgCl2. Hệ thống không suy Mg từ Ca; cần test Mg.");
        }
        else if (!req.MagnesiumMeasured)
        {
            warnings.Add("Mg hiện tại là số ƯỚC LƯỢNG, không phải kết quả test — liều MgCl2 chỉ mang tính tham khảo.");
        }

        if (mgDeficit is < 0)
            warnings.Add($"Mg đang VƯỢT mục tiêu {Math.Abs(mgDeficit.Value):0.##} mg/L — không châm, kiểm tra lại nguồn nước.");

        // ── Tổng Ca+Mg: tổng quan trọng NGANG tỷ lệ, quá cao gây khó lột ──────
        // Chỉ tính khi biết cả hai; không suy giá trị thiếu.
        var totalTarget = caTarget is null || mgTarget is null
            ? (decimal?)null
            : caTarget + mgTarget;
        var totalCurrent = req.CalciumCurrentMgL is null || req.MagnesiumCurrentMgL is null
            ? (decimal?)null
            : req.CalciumCurrentMgL + req.MagnesiumCurrentMgL;

        // Cảnh báo tổng dựa trên mục tiêu hiệu dụng ⇒ chỉ xuất hiện một lần, không trùng.
        var highTotalWarning = HighTotalWarning(totalTarget);
        if (highTotalWarning is not null) warnings.Add(highTotalWarning);

        // ── Độ mặn: chỉ so sánh, không đụng vào phép tính gram ────────────────
        var (salinityStatus, salinityLabel) = CompareSalinity(
            req.SalinityCurrentPpt, req.SalinityTargetPpt);

        // ── Chia liều: châm nhiều lần để không sốc cua ────────────────────────
        var maxStep = req.MaxIncreasePerDoseMgL ?? DefaultMaxIncreasePerDoseMgL;
        if (maxStep <= 0) maxStep = DefaultMaxIncreasePerDoseMgL;

        var (doseCount, caPerDose, mgPerDose) =
            SplitDoses(caDeficit, mgDeficit, caDose, mgDose, maxStep);

        var instructions = DosingInstructions(
            doseCount,
            maxStep,
            caDose is > 0,
            mgDose is > 0,
            req.SalinityCurrentPpt);

        if (doseCount > ComfortableDoseCount)
        {
            warnings.Add(
                $"Cần {doseCount} lần châm — nhiều hơn {ComfortableDoseCount} lần nên chương trình "
                + "sẽ kéo dài. Cân nhắc nâng trần mỗi lần, hạ mục tiêu, hoặc kiểm tra lại số đo "
                + "Ca/Mg xem có sai không.");
        }

        return new MineralDoseResult(
            TargetMode: mode,
            UsedRecommendedTargets: usedRecommended,
            RecommendedCalciumMgL: recCa,
            RecommendedMagnesiumMgL: recMg,
            RecommendedCaMgRatio: RatioString(recCa, recMg),
            CalciumTargetMgL: caTarget,
            MagnesiumTargetMgL: mgTarget,
            CalciumMagnesiumTotalTargetMgL: totalTarget,
            CalciumCurrentMgL: req.CalciumCurrentMgL,
            MagnesiumCurrentMgL: req.MagnesiumCurrentMgL,
            CalciumCurrentSource: caSource,
            MagnesiumCurrentSource: mgSource,
            CalciumMagnesiumTotalCurrentMgL: totalCurrent,
            CalciumDeficitMgL: Round2(caDeficit),
            MagnesiumDeficitMgL: Round2(mgDeficit),
            CaCl2DoseGrams: caDose,
            MgCl2DoseGrams: mgDose,
            CaCl2Product: CaCl2Product,
            MgCl2Product: MgCl2Product,
            SalinityStatus: salinityStatus,
            SalinityStatusLabel: salinityLabel,
            MaxIncreasePerDoseMgL: maxStep,
            DoseCount: doseCount,
            CaCl2GramsPerDose: caPerDose,
            MgCl2GramsPerDose: mgPerDose,
            DoseIntervalHours: DefaultDoseIntervalHours,
            DosingInstructions: instructions,
            Warnings: warnings);
    }

    /// <summary>
    /// Liều = max(0, mục tiêu − hiện tại) × thể tích / hệ số sản phẩm.
    /// Chênh lệch trả về vẫn giữ dấu (âm = đang vượt mục tiêu) để UI giải thích được.
    /// </summary>
    private static (decimal? Deficit, decimal? Dose) DeficitAndDose(
        decimal? current, decimal? target, decimal volumeL, double divisor)
    {
        if (current is null || target is null) return (null, null);

        var deficit = target.Value - current.Value;
        var dose = deficit > 0
            ? Math.Round(deficit * volumeL / (decimal)divisor, 1)
            : 0m;
        return (deficit, dose);
    }

    /// <summary>
    /// So sánh độ mặn hiện tại với mục tiêu. KHÔNG dùng để tính liều khoáng.
    ///
    /// Phân biệt ba tình trạng, không gộp thành một câu "chưa đủ dữ liệu":
    ///   · chưa nhập gì                    → <see cref="SalinityUnknown"/>
    ///   · có độ mặn hiện tại, thiếu mục tiêu → <see cref="SalinityNoTarget"/> (vẫn báo số đã nhập)
    ///   · có cả hai                       → OK / LOW / HIGH
    /// </summary>
    public static (string Status, string Label) CompareSalinity(decimal? current, decimal? target)
    {
        if (current is null && target is null)
            return (SalinityUnknown, "Chưa nhập độ mặn");

        // Thiếu mục tiêu nhưng CÓ số hiện tại: báo đúng số đã nhập, đừng nói "chưa có dữ liệu".
        if (target is null)
            return (SalinityNoTarget,
                $"Độ mặn hiện tại {current:0.##}‰ — chưa nhập mức mong muốn để so sánh");

        if (current is null)
            return (SalinityUnknown,
                $"Có mức mong muốn {target:0.##}‰ nhưng chưa nhập độ mặn hiện tại để so sánh");

        if (current < target) return (SalinityLow, $"Độ mặn thấp hơn mục tiêu ({current:0.##} < {target:0.##}‰)");
        if (current > target) return (SalinityHigh, $"Độ mặn cao hơn mục tiêu ({current:0.##} > {target:0.##}‰)");
        return (SalinityOk, $"Độ mặn đạt mục tiêu ({current:0.##}‰)");
    }

    /// <summary>
    /// Chia tổng liều thành nhiều lần sao cho mỗi lần nồng độ tăng không quá
    /// <paramref name="maxIncreasePerDoseMgL"/>. Số lần do chênh lệch LỚN NHẤT quyết định,
    /// rồi áp cùng số lần cho cả Ca lẫn Mg — chất nào thiếu ít thì mỗi lần châm ít hơn,
    /// không sao (chỉ chậm hơn, không sai).
    ///
    /// Trả (0, null, null) khi không có gì phải châm.
    /// </summary>
    public static (int Count, decimal? CaPerDose, decimal? MgPerDose) SplitDoses(
        decimal? caDeficit,
        decimal? mgDeficit,
        decimal? caDose,
        decimal? mgDose,
        decimal maxIncreasePerDoseMgL)
    {
        if (maxIncreasePerDoseMgL <= 0)
            throw new ArgumentException("maxIncreasePerDoseMgL must be > 0.");

        // Chỉ chênh lệch DƯƠNG mới cần châm; âm (đang vượt) không kéo số lần lên.
        var biggest = Math.Max(
            caDeficit is > 0 ? caDeficit.Value : 0m,
            mgDeficit is > 0 ? mgDeficit.Value : 0m);

        if (biggest <= 0 || (caDose is null && mgDose is null)) return (0, null, null);

        var count = (int)Math.Ceiling(biggest / maxIncreasePerDoseMgL);
        if (count < 1) count = 1;

        return (count,
            caDose is null ? null : Math.Round(caDose.Value / count, 1),
            mgDose is null ? null : Math.Round(mgDose.Value / count, 1));
    }

    /// <summary>
    /// Các bước pha &amp; châm theo thứ tự. Trả về ở tầng service để mobile và desktop
    /// dùng chung một hướng dẫn, không phải chép lại ở hai nơi rồi lệch nhau.
    /// </summary>
    public static List<string> DosingInstructions(
        int doseCount,
        decimal maxIncreasePerDoseMgL,
        bool doseCa,
        bool doseMg,
        decimal? salinityPpt)
    {
        var steps = new List<string>();

        if (doseCount <= 0)
        {
            steps.Add("Không cần châm: Ca/Mg đã đạt hoặc vượt mục tiêu. Đo lại định kỳ thay vì châm thêm.");
            return steps;
        }

        steps.Add(
            $"Chia thành {doseCount} lần, cách nhau tối thiểu {DefaultDoseIntervalHours} giờ "
            + $"(1 lần/ngày). Mỗi lần chỉ tăng tối đa {maxIncreasePerDoseMgL:0.##} mg/L "
            + "để cua không bị sốc thẩm thấu.");

        // Hai muối phải hoà tan RIÊNG: trộn chung ở dạng đậm đặc sẽ kết tủa và toả nhiệt.
        if (doseCa && doseMg)
        {
            steps.Add(
                "Hoà tan RIÊNG từng muối trong hai xô nước bể. TUYỆT ĐỐI không trộn CaCl₂ và "
                + "MgCl₂ chung một xô ở dạng đậm đặc — hai muối gặp nhau ở nồng độ cao sẽ kết tủa "
                + "và toả nhiệt, vừa mất khoáng vừa nóng.");
        }
        else
        {
            steps.Add("Hoà tan muối trong xô nước bể, khuấy cho tan hết trước khi châm.");
        }

        if (doseCa)
        {
            steps.Add(
                "CaCl₂·2H₂O tan RA NHIỆT: cho từ từ và khuấy đều, để dung dịch nguội về nhiệt độ "
                + "bể rồi mới châm. Châm nước nóng làm cua sốc nhiệt.");
        }

        steps.Add(
            "Châm rải đều mặt bể hoặc đổ vào ngăn có dòng chảy mạnh (bể lọc/sump). KHÔNG đổ trực "
            + "tiếp lên cua, không đổ thẳng xuống hộp nuôi.");

        steps.Add(
            "Châm buổi sáng. Tránh chiều tối và ban đêm vì oxy hoà tan thấp nhất, cua đang lột "
            + "thì hoãn lần châm đó lại.");

        steps.Add(
            "Đo lại Ca/Mg VÀ pH trước mỗi lần châm kế tiếp. CaCl₂/MgCl₂ hơi chua nên pH có thể "
            + "giảm nhẹ — đạt mục tiêu thì dừng, không châm cho hết số lần.");

        if (salinityPpt is > ResearchMaxSalinityPpt)
        {
            steps.Add(
                $"Độ mặn {salinityPpt:0.##}‰ nằm NGOÀI vùng đã nghiên cứu, nên mục tiêu "
                + "1200 mg/L Mg là ngoại suy. Đừng châm cho đủ con số: châm từng lần rồi đo, "
                + "dừng ở mức cua khoẻ và lột bình thường.");
        }

        steps.Add(
            "Giữ nước thật ổn định trong thời gian châm: không thay nước lớn, không đổi muối, "
            + "không châm chung với thuốc xử lý nước khác trong cùng ngày.");

        return steps;
    }

    /// <summary>Chuẩn hoá Target Mode. Rỗng ⇒ auto; giá trị lạ ⇒ lỗi rõ ràng thay vì đoán.</summary>
    public static string NormalizeMode(string? raw)
    {
        var m = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return m switch
        {
            "" or ModeAuto => ModeAuto,
            ModeManual => ModeManual,
            _ => throw new ArgumentException("TargetMode must be 'auto' or 'manual'.")
        };
    }

    private static string SourceOf(decimal? current, bool measured)
    {
        if (current is null) return SourceMissing;
        return measured ? SourceMeasured : SourceEstimated;
    }

    /// <summary>
    /// Cảnh báo thuộc về ĐỘ MẶN/BAND, không phụ thuộc mục tiêu cụ thể.
    /// Tách riêng để <see cref="Recommend"/> và <see cref="Calculate"/> dùng chung mà
    /// không kéo cảnh báo tổng của đề xuất vào trường hợp người dùng đặt mục tiêu riêng.
    /// </summary>
    private static List<string> SalinityBandWarnings(decimal? salinityPpt)
    {
        var warnings = new List<string>();
        if (salinityPpt is null) return warnings;

        if (salinityPpt > ResearchMaxSalinityPpt)
            warnings.Add($"Độ mặn {salinityPpt:0.##}‰ vượt vùng đã kiểm chứng trực tiếp (> {ResearchMaxSalinityPpt:0.##}‰) — mục tiêu 400/1200 là ngoại suy, chưa có số liệu đối chứng.");

        if (salinityPpt < LowSalinityBandPpt)
            warnings.Add($"Độ mặn {salinityPpt:0.##}‰ → 150/450, đúng mức tối ưu đã kiểm chứng ở 1,5‰ (Qin et al. 2026, Aquac. Rep. 49:103696). Đây vẫn là ĐỀ XUẤT, không bắt buộc.");

        return warnings;
    }

    /// <summary>Tỷ lệ Ca:Mg dạng "1:3" từ hai mục tiêu.</summary>
    private static string? RatioString(decimal? ca, decimal? mg)
    {
        if (ca is not > 0 || mg is null) return null;
        return $"1:{mg.Value / ca.Value:0.##}";
    }

    /// <summary>
    /// Cảnh báo khi tổng Ca+Mg vượt xa vùng đã kiểm chứng (600 mg/L ở 1,5‰).
    /// Dùng chung cho cả đề xuất lẫn tính liều; nơi gọi quyết định truyền tổng nào
    /// để cảnh báo luôn khớp với mục tiêu thực sự đem đi châm.
    /// </summary>
    private static string? HighTotalWarning(decimal? totalMgL)
    {
        if (totalMgL is null || totalMgL < HighTotalWarningMgL) return null;

        return $"Tổng Ca+Mg {totalMgL.Value:0} mg/L vượt xa vùng đã kiểm chứng "
            + $"({ValidatedOptimalTotalMgL:0} mg/L ở 1,5‰). Ở 1,5‰ tổng 900 mg/L với tỷ lệ 1:4 làm chết "
            + "toàn bộ, và Ca/Mg quá cao gây khó lột. RAS tích tụ Ca/Mg — nên chia thành nhiều lần "
            + "châm rồi đo lại, không châm hết một lần.";
    }

    private static decimal? Round2(decimal? v) => v is null ? null : Math.Round(v.Value, 2);

    private static void Validate(MineralDoseRequest req)
    {
        if (req.WaterVolumeL <= 0)
            throw new ArgumentException("WaterVolumeL must be > 0.");

        RequireNonNegative(req.SalinityCurrentPpt, nameof(req.SalinityCurrentPpt));
        RequireNonNegative(req.SalinityTargetPpt, nameof(req.SalinityTargetPpt));
        RequireNonNegative(req.CalciumCurrentMgL, nameof(req.CalciumCurrentMgL));
        RequireNonNegative(req.CalciumTargetMgL, nameof(req.CalciumTargetMgL));
        RequireNonNegative(req.MagnesiumCurrentMgL, nameof(req.MagnesiumCurrentMgL));
        RequireNonNegative(req.MagnesiumTargetMgL, nameof(req.MagnesiumTargetMgL));
    }

    private static void RequireNonNegative(decimal? value, string name)
    {
        if (value is < 0)
            throw new ArgumentException($"{name} must be >= 0.");
    }
}
