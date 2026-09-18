using CrabSenseBE.Application.DTOs.Water;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Bộ tính PHA ĐỘ MẶN cho nước RAS (Scylla paramamosain).
/// Hàm thuần — không DI, không DB, test trực tiếp như <see cref="MineralDosingCalculator"/>
/// và <see cref="KnConditionEngine"/>.
///
/// BÀI TOÁN KHÁC HẲN liều khoáng Ca/Mg:
///   · Tăng độ mặn = thêm MUỐI          → bài toán khối lượng muối.
///   · Giảm độ mặn = thêm NƯỚC NGỌT     → bài toán pha loãng (bảo toàn khối muối).
///   · Ca/Mg KHÔNG tham gia phép tính, vì không biết thành phần ion của lô muối.
///     Pha xong phải ĐO LẠI Ca/Mg rồi mới tính liều khoáng (<c>NextSteps</c>).
///
/// ── Tăng: quy ước thực địa 1‰ ≈ 1 g muối/L ──────────────────────────────────
/// 1 g muối/L ≈ 1‰ (1 g/kg nước) nên với nước có khối lượng riêng ~1 kg/L:
///
///     muối lý thuyết (kg) = ΔS(‰) × V(L) / 1000
///
/// ponytail: đây là quy ước THỰC ĐỊA mà nông dân và người chơi thuỷ sinh dùng, KHÔNG
/// phải phép cân bằng khối lượng chặt. Cân đúng (‰ tính trên kg dung dịch, thêm muối
/// làm tăng khối lượng) cho ΔS = 10‰ ở 1.000 L là 10,20 kg thay vì 10,00 kg — lệch 2%,
/// nhỏ hơn hẳn sai số thật của muối thô (±10% độ tinh khiết) và khúc xạ kế (±1–2‰).
/// Giữ quy ước thực địa để con số khớp với cách trại vẫn làm; muốn chính xác hơn thì
/// phải biết khối lượng riêng và thành phần ion của lô muối — ngoài phạm vi công cụ này.
///
/// ── Giảm: bảo toàn khối muối ────────────────────────────────────────────────
///     S₁·V₁ = S₂·V₂   ⇒   V₂ = V₁·S₁/S₂
///   · cách A (thêm nước ngọt)  : thêm V₂ − V₁ lít, thể tích TĂNG lên V₂
///   · cách B (thay nước)       : rút V₁(1 − S₂/S₁) lít rồi châm lại đúng lượng
///                                nước ngọt đó, thể tích GIỮ NGUYÊN
///
/// CẢNH BÁO SỐC THẨM THẤU: đổi độ mặn là đổi áp suất thẩm thấu của toàn bộ nước bể,
/// nặng hơn nhiều so với châm khoáng. Cua đang lột/mềm rất nhạy. Vì vậy luôn chia
/// nhiều lần theo <see cref="DefaultMaxChangePerBatchPpt"/>.
/// </summary>
public static class SalinityMixingCalculator
{
    public const string DirIncrease = "INCREASE";
    public const string DirDecrease = "DECREASE";
    public const string DirHold = "HOLD";
    public const string DirUnknown = "UNKNOWN";

    /// <summary>
    /// Trần ĐỔI độ mặn mỗi lần pha (‰) — mặc định 3.
    ///
    /// ĐÂY LÀ NGƯỠNG AN TOÀN VẬN HÀNH, KHÔNG PHẢI SỐ LIỆU NGHIÊN CỨU.
    /// Không có bài nào thử tốc độ đổi độ mặn trên Scylla paramamosain để trích dẫn.
    ///
    /// 3‰ được chọn vì hai lý do vận hành:
    ///   1. Chia 10‰ thành 4 lần — khớp cách trại vẫn làm và khớp ví dụ "chia 3–4 lần"
    ///      cho 11 kg muối.
    ///   2. Đủ nhỏ để mỗi lần đo lại vẫn kịp phát hiện sai (muối thô ẩm, đo nhầm vị trí).
    /// Giảm xuống nữa thì an toàn hơn, chỉ tốn thời gian.
    /// </summary>
    public const decimal DefaultMaxChangePerBatchPpt = 3m;

    /// <summary>
    /// Số giờ tối thiểu giữa hai lần pha. Muối tan và nước đồng nhất cần thời gian;
    /// đây cũng là khoảng để đo lại độ mặn trước khi quyết định lần kế tiếp.
    /// </summary>
    public const int BatchIntervalHours = 12;

    /// <summary>
    /// Số lần pha còn thực tế. Vượt mức này thì nên đổi nguồn nước (nước mặn sẵn có,
    /// hoặc thay nước nhiều lần) chứ không phải pha dồn cho đủ số lần.
    /// </summary>
    public const int ComfortableBatchCount = 7;

    /// <summary>
    /// Thay đổi độ mặn từ mức này trở lên (‰) coi là lớn, luôn kèm cảnh báo chia lần.
    /// </summary>
    public const decimal BigChangePpt = 5m;

    /// <summary>Danh mục loại muối cho UI — một nguồn chung cho mobile và desktop.</summary>
    public static IReadOnlyList<SaltTypeDto> SaltTypes() => SalinitySaltTypes.All;

    /// <summary>Tính lượng muối (tăng ‰) hoặc lượng nước ngọt (giảm ‰) kèm hướng dẫn pha.</summary>
    public static SalinityMixResult Calculate(SalinityMixRequest req)
    {
        Validate(req);

        var salt = SalinitySaltTypes.Resolve(req.SaltType);
        var warnings = new List<string>();

        // ── Thiếu dữ liệu: nói rõ đang thiếu cái nào, đừng gộp thành "chưa có dữ liệu" ──
        if (req.CurrentSalinityPpt is null || req.TargetSalinityPpt is null)
            return Unknown(req, salt, warnings);

        var current = req.CurrentSalinityPpt.Value;
        var target = req.TargetSalinityPpt.Value;
        var delta = target - current;

        var maxStep = req.MaxChangePerBatchPpt ?? DefaultMaxChangePerBatchPpt;
        if (maxStep <= 0) maxStep = DefaultMaxChangePerBatchPpt;

        // ── Không cần làm gì ───────────────────────────────────────────────────
        if (delta == 0)
        {
            return new SalinityMixResult(
                Direction: DirHold,
                DirectionLabel: "Độ mặn đã đạt mục tiêu",
                CurrentSalinityPpt: current,
                TargetSalinityPpt: target,
                DeltaPpt: 0m,
                WaterVolumeL: req.WaterVolumeL,
                TheoreticalSaltKg: null,
                ActualSaltKg: null,
                SaltPurityPercent: req.SaltPurityPercent,
                PurityAssumed: false,
                SaltType: salt.Key,
                SaltTypeLabel: salt.Label,
                SaltTypeNote: salt.Note,
                FreshwaterToAddL: null,
                FinalVolumeL: null,
                WaterToReplaceL: null,
                MaxChangePerBatchPpt: maxStep,
                BatchCount: 0,
                SaltKgPerBatch: null,
                FreshwaterLPerBatch: null,
                BatchIntervalHours: BatchIntervalHours,
                Instructions:
                [
                    $"Nước đang đúng {current:0.##}‰ — không cần thêm muối hay nước ngọt.",
                    "Đo lại độ mặn định kỳ: nước mưa, nước bốc hơi và nước thay đều làm lệch số này.",
                ],
                Warnings: warnings,
                NextSteps: NextSteps());
        }

        return delta > 0
            ? Increase(req, salt, current, target, delta, maxStep, warnings)
            : Decrease(req, salt, current, target, delta, maxStep, warnings);
    }

    // ── Tăng độ mặn: muối lý thuyết → muối thực tế theo độ tinh khiết ────────
    private static SalinityMixResult Increase(
        SalinityMixRequest req,
        SaltTypeDto salt,
        decimal current,
        decimal target,
        decimal delta,
        decimal maxStep,
        List<string> warnings)
    {
        var theoreticalKg = Math.Round(delta * req.WaterVolumeL / 1000m, 2);

        // Không có số độ tinh khiết ⇒ KHÔNG đoán bừa (mỗi lô muối thô một khác).
        // Tính theo 100% và nói rõ đây là mức TỐI THIỂU.
        var purity = req.SaltPurityPercent;
        var assumed = purity is null;
        var effective = purity is null ? 1m : purity.Value / 100m;
        var actualKg = Math.Round(theoreticalKg / effective, 2);

        var batches = BatchCount(delta, maxStep);
        var perBatch = batches > 0 ? Math.Round(actualKg / batches, 2) : (decimal?)null;

        // ── Cảnh báo ──────────────────────────────────────────────────────────
        if (assumed)
        {
            warnings.Add(
                $"Chưa biết độ tinh khiết của lô muối nên hệ thống tính theo 100%: {actualKg:0.##} kg là mức "
                + "TỐI THIỂU. Muối thô thực tế phải dùng nhiều hơn — hãy cân theo hướng dẫn rồi ĐO LẠI độ mặn "
                + "và hiệu chỉnh, đừng tin con số trên giấy.");
        }

        if (salt.Key == SalinitySaltTypes.Raw)
        {
            warnings.Add(
                "Muối thô có đất, cát, hữu cơ và độ ẩm nên thành phần không ổn định: số trên là ƯỚC LƯỢNG. "
                + "Hoà tan rồi để lắng (hoặc lọc) trước khi châm để tránh đưa cặn vào bể.");
        }

        if (salt.Key is SalinitySaltTypes.Table or SalinitySaltTypes.Raw)
        {
            warnings.Add(
                (salt.Key == SalinitySaltTypes.Table
                    ? "NaCl chỉ thêm Na và Cl."
                    : "Muối thô chủ yếu là NaCl.")
                + " Ca/Mg/KH KHÔNG tăng theo độ mặn, nên lên mức mặn mới phải ĐO LẠI Ca/Mg rồi mới tính liều "
                + "khoáng — tuyệt đối không suy Ca/Mg từ độ mặn.");
        }

        if (target > MineralDosingCalculator.ResearchMaxSalinityPpt)
        {
            warnings.Add(
                $"Độ mặn mục tiêu {target:0.##}‰ vượt vùng đã nghiên cứu trực tiếp cho Scylla paramamosain "
                + $"(≤ {MineralDosingCalculator.ResearchMaxSalinityPpt:0.##}‰). Kiểm tra lại ngưỡng của trại "
                + "và tăng thật chậm, theo dõi cua ăn và lột.");
        }

        if (delta >= BigChangePpt)
        {
            warnings.Add(
                $"Cần đổi tới {delta:0.##}‰ — thay đổi lớn với áp suất thẩm thấu của cả bể. Bắt buộc chia "
                + $"{batches} lần, mỗi lần không quá {maxStep:0.##}‰, cách nhau tối thiểu {BatchIntervalHours} giờ.");
        }

        if (batches > ComfortableBatchCount)
        {
            warnings.Add(
                $"Cần {batches} lần pha — nhiều hơn {ComfortableBatchCount} lần nên kéo dài. Cân nhắc nâng trần "
                + "mỗi lần, đổi sang nguồn nước mặn sẵn có, hoặc kiểm tra lại số đo độ mặn.");
        }

        return new SalinityMixResult(
            Direction: DirIncrease,
            DirectionLabel: $"Cần TĂNG {delta:0.##}‰",
            CurrentSalinityPpt: current,
            TargetSalinityPpt: target,
            DeltaPpt: delta,
            WaterVolumeL: req.WaterVolumeL,
            TheoreticalSaltKg: theoreticalKg,
            ActualSaltKg: actualKg,
            SaltPurityPercent: purity,
            PurityAssumed: assumed,
            SaltType: salt.Key,
            SaltTypeLabel: salt.Label,
            SaltTypeNote: salt.Note,
            FreshwaterToAddL: null,
            FinalVolumeL: null,
            WaterToReplaceL: null,
            MaxChangePerBatchPpt: maxStep,
            BatchCount: batches,
            SaltKgPerBatch: perBatch,
            FreshwaterLPerBatch: null,
            BatchIntervalHours: BatchIntervalHours,
            Instructions: IncreaseInstructions(
                req.WaterVolumeL, current, target, delta, actualKg, batches, perBatch, maxStep, salt),
            Warnings: warnings,
            NextSteps: NextSteps());
    }

    // ── Giảm độ mặn: pha loãng, bảo toàn khối muối ──────────────────────────
    private static SalinityMixResult Decrease(
        SalinityMixRequest req,
        SaltTypeDto salt,
        decimal current,
        decimal target,
        decimal delta,
        decimal maxStep,
        List<string> warnings)
    {
        // V₂ = V₁·S₁/S₂  →  nước ngọt thêm vào = V₂ − V₁
        var finalVolume = Math.Round(req.WaterVolumeL * current / target, 1);
        var freshwater = Math.Round(finalVolume - req.WaterVolumeL, 1);

        // Rút rồi châm lại đúng lượng đó ⇒ giữ nguyên thể tích: V₁(1 − S₂/S₁)
        var replace = Math.Round(req.WaterVolumeL * (1m - target / current), 1);

        var change = Math.Abs(delta);
        var batches = BatchCount(change, maxStep);
        var perBatch = batches > 0 ? Math.Round(freshwater / batches, 1) : (decimal?)null;

        // ── Cảnh báo ──────────────────────────────────────────────────────────
        if (freshwater > req.WaterVolumeL)
        {
            warnings.Add(
                $"Cần thêm {freshwater:0.#} L nước ngọt — nhiều hơn cả thể tích đang có, nên tổng thể tích sẽ "
                + $"lên {finalVolume:0.#} L. Kiểm tra sức chứa hệ thống trước khi làm, hoặc dùng cách THAY NƯỚC "
                + $"(rút {replace:0.#} L rồi châm lại nước ngọt) để giữ nguyên thể tích.");
        }

        if (change >= BigChangePpt)
        {
            warnings.Add(
                $"Cần đổi tới {change:0.##}‰ — thay đổi lớn với áp suất thẩm thấu của cả bể. Bắt buộc chia "
                + $"{batches} lần, mỗi lần không quá {maxStep:0.##}‰, cách nhau tối thiểu {BatchIntervalHours} giờ.");
        }

        if (batches > ComfortableBatchCount)
        {
            warnings.Add(
                $"Cần {batches} lần pha — nhiều hơn {ComfortableBatchCount} lần nên kéo dài. Cân nhắc hạ mục "
                + "tiêu gần hơn, hoặc thay nước nhiều lần thay vì pha loãng một lần.");
        }

        warnings.Add(
            "Nước ngọt làm loãng CẢ Ca/Mg/KH, không chỉ NaCl. Hạ được độ mặn rồi phải đo lại Ca/Mg rồi mới "
            + "tính liều khoáng.");

        return new SalinityMixResult(
            Direction: DirDecrease,
            DirectionLabel: $"Cần GIẢM {change:0.##}‰",
            CurrentSalinityPpt: current,
            TargetSalinityPpt: target,
            DeltaPpt: delta,
            WaterVolumeL: req.WaterVolumeL,
            // Giảm độ mặn không dùng muối: để null, không trả 0 để UI không hiện "0 kg muối".
            TheoreticalSaltKg: null,
            ActualSaltKg: null,
            SaltPurityPercent: req.SaltPurityPercent,
            PurityAssumed: false,
            SaltType: salt.Key,
            SaltTypeLabel: salt.Label,
            SaltTypeNote: salt.Note,
            FreshwaterToAddL: freshwater,
            FinalVolumeL: finalVolume,
            WaterToReplaceL: replace,
            MaxChangePerBatchPpt: maxStep,
            BatchCount: batches,
            SaltKgPerBatch: null,
            FreshwaterLPerBatch: perBatch,
            BatchIntervalHours: BatchIntervalHours,
            Instructions: DecreaseInstructions(
                req.WaterVolumeL, current, target, change, freshwater, finalVolume, replace,
                batches, perBatch, maxStep),
            Warnings: warnings,
            NextSteps: NextSteps());
    }

    // ── Hướng dẫn pha: TĂNG độ mặn ──────────────────────────────────────────
    private static List<string> IncreaseInstructions(
        decimal volumeL,
        decimal current,
        decimal target,
        decimal delta,
        decimal actualKg,
        int batches,
        decimal? perBatchKg,
        decimal maxStep,
        SaltTypeDto salt)
    {
        return
        [
            $"Chuẩn bị: nước hiện tại {volumeL:0.#} L ở {current:0.##}‰; {actualKg:0.##} kg {salt.Label}; "
            + "một thùng/bồn pha RIÊNG; bơm tuần hoàn; máy đo độ mặn hoặc khúc xạ kế.",

            "TUYỆT ĐỐI không đổ muối khô trực tiếp xuống bể cua: muối chưa tan sẽ chìm vào hộp nuôi và "
            + "cháy mang cua tại chỗ.",

            $"Lấy một phần nước bể ra thùng pha riêng (khoảng 10–20% thể tích bể, tức "
            + $"{Math.Round(volumeL * 0.15m, 0):0} L), rồi mới cho muối vào thùng đó.",

            "Cho muối TỪ TỪ và bật bơm khuấy cho tan hoàn toàn. Muối thô để lắng 15–30 phút rồi chỉ châm "
            + "phần nước trong, bỏ cặn đáy thùng.",

            $"Chia thành {batches} lần, mỗi lần tối đa {perBatchKg:0.##} kg (đổi không quá {maxStep:0.##}‰), "
            + $"cách nhau tối thiểu {BatchIntervalHours} giờ để cua kịp thích nghi áp suất thẩm thấu.",

            "Sau MỖI lần: châm rải đều mặt bể hoặc đổ vào ngăn có dòng chảy mạnh (bể lọc/sump), chạy tuần "
            + "hoàn cho nước trộn đều rồi ĐO LẠI độ mặn ở 2–3 vị trí khác nhau.",

            "Dừng khi còn cách mục tiêu khoảng 0,5‰ rồi thêm một lượng nhỏ và đo lại. Nếu máy đo cho kết "
            + "quả khác con số tính toán thì TIN MÁY ĐO — muối thô ẩm và lẫn tạp nên con số chỉ là hướng dẫn.",

            "Châm vào buổi sáng, tránh chiều tối và ban đêm vì oxy hoà tan thấp nhất. Cua đang lột hoặc vỏ "
            + "mềm thì hoãn lần pha đó lại.",

            $"Không đổi đồng thời thứ khác trong thời gian pha: không thay nước lớn, không châm khoáng "
            + $"Ca/Mg, không dùng thuốc xử lý nước cùng ngày. Đưa từ {current:0.##}‰ lên {target:0.##}‰ "
            + $"({delta:0.##}‰) là đủ để cua mệt nếu làm cùng lúc với việc khác.",
        ];
    }

    // ── Hướng dẫn pha: GIẢM độ mặn ──────────────────────────────────────────
    private static List<string> DecreaseInstructions(
        decimal volumeL,
        decimal current,
        decimal target,
        decimal change,
        decimal freshwaterL,
        decimal finalVolumeL,
        decimal replaceL,
        int batches,
        decimal? perBatchL,
        decimal maxStep)
    {
        return
        [
            $"Chuẩn bị: {volumeL:0.#} L nước ở {current:0.##}‰ và nước ngọt ĐÃ KHỬ CLO (phơi nắng 24–48 giờ "
            + "hoặc dùng dung dịch khử clo). Clo còn dư là chất diệt cua con và làm cua mất vỏ.",

            $"Cách A — THÊM nước ngọt (thể tích TĂNG): cần {freshwaterL:0.#} L nước ngọt, tổng thể tích "
            + $"thành {finalVolumeL:0.#} L. Kiểm tra bể/bồn chịu được mức nước mới trước khi làm.",

            $"Cách B — THAY nước (thể tích GIỮ NGUYÊN): rút {replaceL:0.#} L nước bể ra rồi châm lại đúng "
            + $"{replaceL:0.#} L nước ngọt. Dùng cách này khi không muốn dâng mực nước, nhưng phải làm nhiều "
            + "lần hơn vì mỗi lần chỉ hạ được một phần.",

            $"Chia thành {batches} lần, mỗi lần hạ không quá {maxStep:0.##}‰ (mỗi lần khoảng "
            + $"{perBatchL:0.#} L nước ngọt), cách nhau tối thiểu {BatchIntervalHours} giờ để cua kịp thích "
            + "nghi áp suất thẩm thấu.",

            "Châm nước ngọt từ từ, rải đều mặt bể hoặc đổ vào ngăn có dòng chảy mạnh. KHÔNG đổ thẳng lên "
            + "cua, không đổ thẳng xuống hộp nuôi.",

            "Nhiệt độ nước ngọt thêm vào phải gần nhiệt độ bể. Nước ngọt hơn 2–3 °C làm cua sốc nhiệt, dễ "
            + "lột sớm hoặc mềm vỏ.",

            "Sau MỖI lần: chạy tuần hoàn cho nước trộn đều rồi ĐO LẠI độ mặn ở 2–3 vị trí. Đạt gần mục tiêu "
            + "thì chuyển sang thêm lượng nhỏ và đo lại.",

            $"Không hạ quá nhanh và không hạ sâu hơn mục tiêu {target:0.##}‰. Đổi {change:0.##}‰ liên tục "
            + "trong một ngày là rủi ro thật với cua đang lột.",

            "Châm buổi sáng và giữ nước ổn định trong thời gian hạ độ mặn: không châm khoáng, không dùng "
            + "thuốc xử lý nước khác cùng ngày.",
        ];
    }

    // ── Việc phải làm SAU khi pha: nối sang liều khoáng Ca/Mg ───────────────
    private static List<string> NextSteps() =>
    [
        "② ĐO LẠI sau khi nước đã trộn đều (chờ vài giờ, đo ở 2–3 vị trí): độ mặn, pH, Ca, Mg, KH. "
        + "Vừa pha xong nước chưa đồng nhất nên đo ngay sẽ ra số sai.",

        "③ Độ mặn đạt rồi mới tính liều khoáng: mở tab «Ca/Mg», nhập Ca/Mg vừa đo để tính CaCl₂/MgCl₂. "
        + "NaCl và muối thô không tăng Ca/Mg theo độ mặn.",

        "④ Kiểm tra cuối trước khi thả hoặc thêm cua: độ mặn ✓ Ca ✓ Mg ✓ pH ✓ KH ✓.",
    ];

    /// <summary>
    /// Số lần pha để mỗi lần đổi không quá <paramref name="maxChangePerBatchPpt"/>. 0 khi không phải đổi gì.
    /// </summary>
    public static int BatchCount(decimal changePpt, decimal maxChangePerBatchPpt)
    {
        if (maxChangePerBatchPpt <= 0)
            throw new ArgumentException("maxChangePerBatchPpt must be > 0.");
        if (changePpt <= 0) return 0;

        var count = (int)Math.Ceiling(changePpt / maxChangePerBatchPpt);
        return count < 1 ? 1 : count;
    }

    /// <summary>Kết quả khi thiếu độ mặn — nói rõ thiếu ô nào để UI chỉ đúng chỗ cần nhập.</summary>
    private static SalinityMixResult Unknown(
        SalinityMixRequest req,
        SaltTypeDto salt,
        List<string> warnings)
    {
        var current = req.CurrentSalinityPpt;
        var target = req.TargetSalinityPpt;

        warnings.Add("Cần nhập CẢ độ mặn hiện tại và độ mặn mong muốn mới tính được lượng muối hoặc nước ngọt.");

        if (current is not null)
            warnings.Add($"Đã có độ mặn hiện tại {current:0.##}‰ — chỉ còn thiếu mức mong muốn.");
        else if (target is not null)
            warnings.Add($"Đã có mức mong muốn {target:0.##}‰ — chỉ còn thiếu độ mặn hiện tại.");

        return new SalinityMixResult(
            Direction: DirUnknown,
            DirectionLabel: "Chưa đủ dữ liệu độ mặn",
            CurrentSalinityPpt: current,
            TargetSalinityPpt: target,
            DeltaPpt: 0m,
            WaterVolumeL: req.WaterVolumeL,
            TheoreticalSaltKg: null,
            ActualSaltKg: null,
            SaltPurityPercent: req.SaltPurityPercent,
            PurityAssumed: false,
            SaltType: salt.Key,
            SaltTypeLabel: salt.Label,
            SaltTypeNote: salt.Note,
            FreshwaterToAddL: null,
            FinalVolumeL: null,
            WaterToReplaceL: null,
            MaxChangePerBatchPpt: req.MaxChangePerBatchPpt ?? DefaultMaxChangePerBatchPpt,
            BatchCount: 0,
            SaltKgPerBatch: null,
            FreshwaterLPerBatch: null,
            BatchIntervalHours: BatchIntervalHours,
            Instructions: [],
            Warnings: warnings,
            NextSteps: NextSteps());
    }

    private static void Validate(SalinityMixRequest req)
    {
        if (req.WaterVolumeL <= 0)
            throw new ArgumentException("WaterVolumeL must be > 0.");

        RequireNonNegative(req.CurrentSalinityPpt, nameof(req.CurrentSalinityPpt));
        RequireNonNegative(req.TargetSalinityPpt, nameof(req.TargetSalinityPpt));

        if (req.SaltPurityPercent is not null and (<= 0m or > 100m))
            throw new ArgumentException("SaltPurityPercent must be in (0, 100].");

        // Giảm về 0‰ đòi hỏi vô hạn nước ngọt — chặn ngay ở tầng vào, đừng để chia cho 0.
        if (req.CurrentSalinityPpt is not null
            && req.TargetSalinityPpt is not null
            && req.TargetSalinityPpt.Value <= 0m
            && req.TargetSalinityPpt.Value < req.CurrentSalinityPpt.Value)
        {
            throw new ArgumentException(
                "TargetSalinityPpt must be > 0 when reducing salinity — cannot reach 0‰ by adding freshwater.");
        }
    }

    private static void RequireNonNegative(decimal? value, string name)
    {
        if (value is < 0) throw new ArgumentException($"{name} must be >= 0.");
    }
}
