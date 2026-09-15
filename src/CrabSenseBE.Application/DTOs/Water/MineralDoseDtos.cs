namespace CrabSenseBE.Application.DTOs.Water;

/// <summary>
/// Input cho bộ tính liều khoáng Ca/Mg (nước RAS). Thuần tính toán — không ghi DB.
///
/// Phân biệt rõ 4 loại giá trị (yêu cầu nghiệp vụ):
///   · measured    — số đo thực từ test  (…Measured = true)
///   · estimated   — số ước lượng, KHÔNG phải test (…Measured = false) ⇒ luôn kèm cảnh báo
///   · recommended — mục tiêu hệ đề xuất theo độ mặn (TargetMode = auto)
///   · calculated  — liều hóa chất suy ra từ chênh lệch (CaCl2/MgCl2 gram)
/// </summary>
public record MineralDoseRequest(
    // L — thể tích nước cần xử lý. Bắt buộc > 0.
    decimal WaterVolumeL,
    // ‰ — độ mặn hiện tại. Dùng để CHỌN mục tiêu và SO SÁNH trạng thái, không dùng để tính gram.
    decimal? SalinityCurrentPpt = null,
    // ‰ — độ mặn mong muốn. Chỉ dùng để báo trạng thái.
    decimal? SalinityTargetPpt = null,
    // mg/L — Ca hiện tại. Bỏ trống ⇒ không tính được liều CaCl2.
    decimal? CalciumCurrentMgL = null,
    // mg/L — Ca mục tiêu. Bỏ trống + TargetMode=auto ⇒ lấy đề xuất theo độ mặn.
    decimal? CalciumTargetMgL = null,
    // mg/L — Mg hiện tại. Bỏ trống ⇒ KHÔNG tính liều MgCl2 (hệ thống không suy Mg từ Ca).
    decimal? MagnesiumCurrentMgL = null,
    // mg/L — Mg mục tiêu. Bỏ trống + TargetMode=auto ⇒ lấy đề xuất theo độ mặn.
    decimal? MagnesiumTargetMgL = null,
    // Ca hiện tại là số đo thật hay số ước lượng?
    bool CalciumMeasured = true,
    // Mg hiện tại là số đo thật hay số ước lượng?
    bool MagnesiumMeasured = true,
    // auto | manual. auto = tự đề xuất mục tiêu còn thiếu; manual = tôn trọng mục tiêu người dùng.
    string TargetMode = "auto",
    // mg/L — trần tăng nồng độ MỖI LẦN châm. Bỏ trống ⇒ dùng mặc định an toàn 50.
    // Đây là ngưỡng an toàn vận hành, không phải số liệu nghiên cứu (xem
    // MineralDosingCalculator.DefaultMaxIncreasePerDoseMgL).
    decimal? MaxIncreasePerDoseMgL = null);

/// <summary>
/// Kết quả tính liều khoáng. Mọi trường số có thể null khi thiếu dữ liệu đầu vào
/// tương ứng — null nghĩa là "chưa tính được", KHÔNG phải 0.
/// </summary>
public record MineralDoseResult(
    // auto | manual
    string TargetMode,
    // true khi có ít nhất một mục tiêu được lấy từ đề xuất thay vì người dùng nhập
    bool UsedRecommendedTargets,
    // Đề xuất theo độ mặn — luôn trả về để UI hiển thị "Recommended target"
    decimal? RecommendedCalciumMgL,
    decimal? RecommendedMagnesiumMgL,
    // Dạng "1:3"
    string? RecommendedCaMgRatio,
    // Mục tiêu thực sự đem đi tính
    decimal? CalciumTargetMgL,
    decimal? MagnesiumTargetMgL,
    // Tổng Ca+Mg mục tiêu (mg/L). Tổng quan trọng ngang tỷ lệ — quá cao gây khó lột.
    decimal? CalciumMagnesiumTotalTargetMgL,
    // Số hiện tại (echo lại để UI đối chiếu)
    decimal? CalciumCurrentMgL,
    decimal? MagnesiumCurrentMgL,
    // measured | estimated | missing
    string CalciumCurrentSource,
    string MagnesiumCurrentSource,
    // Tổng Ca+Mg hiện tại — chỉ có khi đã đo CẢ HAI, tuyệt đối không suy ra
    decimal? CalciumMagnesiumTotalCurrentMgL,
    // mg/L — dương = thiếu (cần châm), âm = đang vượt mục tiêu
    decimal? CalciumDeficitMgL,
    decimal? MagnesiumDeficitMgL,
    // gram sản phẩm thương mại; 0 khi đã đủ/vượt; null khi chưa tính được
    decimal? CaCl2DoseGrams,
    decimal? MgCl2DoseGrams,
    string CaCl2Product,
    string MgCl2Product,
    // SALINITY_OK | SALINITY_LOW | SALINITY_HIGH | SALINITY_UNKNOWN
    string SalinityStatus,
    // Nhãn tiếng Việt cho UI
    string SalinityStatusLabel,
    // ── Kế hoạch chia liều: châm nhiều lần để KHÔNG sốc cua ────────────────
    // mg/L — trần tăng nồng độ mỗi lần châm đã dùng để chia
    decimal MaxIncreasePerDoseMgL,
    // Số lần châm (0 = không cần châm gì). Mỗi lần tăng ≤ MaxIncreasePerDoseMgL.
    int DoseCount,
    // gram mỗi lần châm; null khi không tính được, 0 khi đã đủ (không châm)
    decimal? CaCl2GramsPerDose,
    decimal? MgCl2GramsPerDose,
    // Số giờ tối thiểu giữa hai lần châm
    int DoseIntervalHours,
    // Các bước pha & châm theo thứ tự — dùng chung cho mobile lẫn desktop
    IReadOnlyList<string> DosingInstructions,
    // Cảnh báo: số ước lượng, thiếu Mg, ngoài vùng nghiên cứu, đã vượt mục tiêu…
    IReadOnlyList<string> Warnings);

/// <summary>Đề xuất mục tiêu Ca/Mg theo độ mặn, không cần thể tích nước.</summary>
public record MineralTargetRecommendationDto(
    decimal? SalinityPpt,
    decimal? RecommendedCalciumMgL,
    decimal? RecommendedMagnesiumMgL,
    string? RecommendedCaMgRatio,
    // Tổng Ca+Mg (mg/L) của mục tiêu đề xuất — tổng quan trọng ngang tỷ lệ
    decimal? CalciumMagnesiumTotalMgL,
    // Nhãn tiếng Việt cho UI — nêu rõ đây là giá trị ĐỀ XUẤT, không bắt buộc
    string Note,
    IReadOnlyList<string> Warnings);
