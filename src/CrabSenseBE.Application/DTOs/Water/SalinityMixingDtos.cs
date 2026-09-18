namespace CrabSenseBE.Application.DTOs.Water;

/// <summary>
/// Danh mục loại muối dùng để TĂNG độ mặn. Key dùng trong request; nhãn + ghi chú
/// trả cho UI để mobile và desktop không phải chép lại rồi lệch nhau.
/// </summary>
public static class SalinitySaltTypes
{
    /// <summary>Muối thô / muối biển phơi — rẻ, sẵn có, thành phần không ổn định.</summary>
    public const string Raw = "raw";

    /// <summary>Muối ăn / NaCl tinh khiết — chỉ có Na và Cl.</summary>
    public const string Table = "table";

    /// <summary>Muối biển nhân tạo cho nuôi thuỷ sản — có sẵn Ca/Mg/KH.</summary>
    public const string SeaSaltMix = "sea_salt_mix";

    public static readonly IReadOnlyList<SaltTypeDto> All =
    [
        new SaltTypeDto(
            Raw,
            "Muối thô (muối biển phơi)",
            "Rẻ và sẵn có ở trại, nhưng thành phần thay đổi theo lô: thường 85–95% NaCl, còn đất, cát, "
            + "hữu cơ và độ ẩm. Không có số của nhà sản xuất thì để TRỐNG ô độ tinh khiết.",
            // Không đưa số mặc định cho muối thô: mỗi lô một khác, đoán bừa còn tệ hơn để trống.
            null),
        new SaltTypeDto(
            Table,
            "Muối ăn / NaCl tinh khiết",
            "Tinh khiết cao nhưng CHỈ có Na và Cl: tăng độ mặn mà không tăng Ca/Mg/KH. Không dùng muối i-ốt "
            + "hoặc muối trộn chất chống vón cho bể nuôi.",
            99m),
        new SaltTypeDto(
            SeaSaltMix,
            "Muối biển nhân tạo (sea salt mix)",
            "Loại dùng cho nuôi thuỷ sản: đã có Ca/Mg/KH theo tỷ lệ nước biển nên ít làm lệch khoáng hơn "
            + "NaCl. Đắt hơn nhiều khi pha thể tích lớn.",
            null),
    ];

    /// <summary>Chuẩn hoá key loại muối. Rỗng ⇒ muối thô; key lạ ⇒ lỗi rõ ràng thay vì đoán.</summary>
    public static SaltTypeDto Resolve(string? raw)
    {
        var key = (raw ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");
        if (key.Length == 0) return All[0];

        foreach (var t in All)
        {
            if (t.Key == key) return t;
        }

        var valid = string.Join(", ", All.Select(t => t.Key));
        throw new ArgumentException($"SaltType must be one of: {valid}.");
    }
}

/// <summary>Một loại muối trong danh mục — trả từ <c>GET /api/mineral-dosing/salt-types</c>.</summary>
public record SaltTypeDto(
    string Key,
    string Label,
    string Note,
    // % tinh khiết thường gặp. null = KHÔNG có số đáng tin ⇒ UI để trống ô độ tinh khiết
    // và hệ thống tính theo 100% kèm cảnh báo phải đo lại.
    decimal? TypicalPurityPercent);

/// <summary>
/// Input cho bộ tính PHA ĐỘ MẶN — thuần tính toán, không ghi DB.
///
/// Khác với <see cref="MineralDoseRequest"/> ở chỗ đây là bài toán KHỐI LƯỢNG:
/// thêm muối để tăng ‰, hoặc thêm nước ngọt để giảm ‰. Ca/Mg không tham gia phép
/// tính (không biết thành phần ion của muối), nên phải đo lại sau khi pha rồi mới
/// tính liều khoáng — xem <c>NextSteps</c> trong kết quả.
/// </summary>
public record SalinityMixRequest(
    // L — thể tích nước cần pha. Bắt buộc > 0.
    decimal WaterVolumeL,
    // ‰ — độ mặn ĐO ĐƯỢC của nước hiện tại. Nước đã trộn thì nhập số thực đo, không nhập tỷ lệ pha.
    decimal? CurrentSalinityPpt = null,
    // ‰ — độ mặn mong muốn. Phải > 0 (giảm về 0‰ là bất khả thi bằng cách thêm nước ngọt).
    decimal? TargetSalinityPpt = null,
    // raw | table | sea_salt_mix — chỉ dùng khi CẦN TĂNG độ mặn.
    string SaltType = SalinitySaltTypes.Raw,
    // % — độ tinh khiết của lô muối đang dùng. Bỏ trống ⇒ tính theo 100% (mức tối thiểu) + cảnh báo.
    decimal? SaltPurityPercent = null,
    // ‰ — trần đổi độ mặn MỖI LẦN pha. Bỏ trống ⇒ dùng mặc định an toàn
    // (<see cref="Services.SalinityMixingCalculator.DefaultMaxChangePerBatchPpt"/>).
    decimal? MaxChangePerBatchPpt = null);

/// <summary>
/// Kết quả tính pha độ mặn. Mọi trường số có thể null khi thiếu dữ liệu đầu vào
/// tương ứng — null nghĩa là "chưa tính được", KHÔNG phải 0.
/// </summary>
public record SalinityMixResult(
    // INCREASE | DECREASE | HOLD | UNKNOWN
    string Direction,
    // Nhãn tiếng Việt cho UI
    string DirectionLabel,
    decimal? CurrentSalinityPpt,
    decimal? TargetSalinityPpt,
    // ‰ — dương = cần tăng, âm = cần giảm, 0 = đã đạt (hoặc chưa tính được)
    decimal DeltaPpt,
    decimal WaterVolumeL,

    // ── Tăng độ mặn ────────────────────────────────────────────────────────
    // kg muối nếu muối tinh khiết 100% — mức TỐI THIỂU phải dùng
    decimal? TheoreticalSaltKg,
    // kg muối thực tế theo độ tinh khiết đã nhập (bằng lý thuyết khi tinh khiết 100%)
    decimal? ActualSaltKg,
    // % đã dùng để tính. null = người dùng chưa biết
    decimal? SaltPurityPercent,
    // true = chưa có số tinh khiết nên đã tính theo 100%
    bool PurityAssumed,
    string SaltType,
    string SaltTypeLabel,
    string SaltTypeNote,

    // ── Giảm độ mặn ────────────────────────────────────────────────────────
    // L nước ngọt ĐÃ KHỬ CLO cần thêm — cách này làm TĂNG thể tích lên FinalVolumeL
    decimal? FreshwaterToAddL,
    decimal? FinalVolumeL,
    // L nước cần THAY (rút ra rồi châm lại nước ngọt) — cách này giữ nguyên thể tích
    decimal? WaterToReplaceL,

    // ── Chia lần để không sốc cua ──────────────────────────────────────────
    // ‰ — trần đổi độ mặn mỗi lần đã dùng để chia
    decimal MaxChangePerBatchPpt,
    // Số lần pha (0 = không cần làm gì). Mỗi lần đổi ≤ MaxChangePerBatchPpt
    int BatchCount,
    // kg muối mỗi lần; null khi không tăng hoặc chưa tính được
    decimal? SaltKgPerBatch,
    // L nước ngọt mỗi lần; null khi không giảm hoặc chưa tính được
    decimal? FreshwaterLPerBatch,
    // Số giờ tối thiểu giữa hai lần pha
    int BatchIntervalHours,

    // Các bước pha theo thứ tự — dùng chung cho mobile lẫn desktop
    IReadOnlyList<string> Instructions,
    // Cảnh báo: thiếu dữ liệu, muối thô, NaCl làm lệch khoáng, ngoài vùng nghiên cứu…
    IReadOnlyList<string> Warnings,
    // Việc phải làm SAU khi pha: đo lại số đo, rồi tính liều Ca/Mg, rồi kiểm tra cuối
    IReadOnlyList<string> NextSteps);
