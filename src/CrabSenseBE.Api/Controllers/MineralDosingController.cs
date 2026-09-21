using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Water;
using CrabSenseBE.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Nước RAS — PHA ĐỘ MẶN và LIỀU KHOÁNG Ca/Mg. Thuần tính toán, KHÔNG ghi DB.
///
/// Luồng làm việc hai bước, tách thành hai endpoint vì là hai bài toán khác nhau:
///   1. <c>POST salinity</c> — pha nước: thêm muối để tăng ‰, thêm nước ngọt để giảm ‰.
///   2. <c>POST calculate</c> — châm khoáng: độ mặn chỉ chọn mục tiêu Ca/Mg; liều suy từ
///      chênh lệch (test − mục tiêu). Thiếu Mg thì báo cần đo Mg, không suy từ Ca.
///
/// Pha nước xong mới tính khoáng, vì Ca/Mg thay đổi theo nước mới — xem
/// <c>nextSteps</c> trong kết quả pha độ mặn.
/// </summary>
[ApiController]
[Route("api/mineral-dosing")]
[Authorize]
[Tags("37. Water — Salinity mixing & mineral dosing (Ca/Mg)")]
[Produces("application/json")]
public class MineralDosingController : ControllerBase
{
    /// <summary>[EVAL] Mục tiêu Ca/Mg đề xuất theo độ mặn. Không cần thể tích nước.</summary>
    [HttpGet("targets")]
    public IActionResult Targets([FromQuery] decimal? salinityPpt = null)
        => Ok(ApiResponse<MineralTargetRecommendationDto>.Ok(
            MineralDosingCalculator.Recommend(salinityPpt)));

    /// <summary>
    /// [EVAL] Tính liều CaCl2/MgCl2 từ chênh lệch hiện tại → mục tiêu.
    /// Body: waterVolumeL (bắt buộc) + salinity/Ca/Mg hiện tại và mục tiêu + targetMode.
    /// </summary>
    [HttpPost("calculate")]
    public IActionResult Calculate([FromBody] MineralDoseRequest req)
    {
        try
        {
            return Ok(ApiResponse<MineralDoseResult>.Ok(
                MineralDosingCalculator.Calculate(req)));
        }
        catch (ArgumentException ex)
        {
            // Lỗi đầu vào (thể tích ≤ 0, nồng độ âm, targetMode lạ) ⇒ 400 chứ không phải 500.
            throw AppException.BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Danh mục loại muối dùng khi PHA ĐỘ MẶN. Nhãn + ghi chú để mobile và desktop
    /// không phải chép lại rồi lệch nhau.
    /// </summary>
    [HttpGet("salt-types")]
    public IActionResult SaltTypes()
        => Ok(ApiResponse<IReadOnlyList<SaltTypeDto>>.Ok(
            SalinityMixingCalculator.SaltTypes()));

    /// <summary>
    /// [EVAL] Tính lượng muối (khi cần TĂNG độ mặn) hoặc lượng nước ngọt (khi cần
    /// GIẢM độ mặn), kèm hướng dẫn chia lần và việc phải đo lại.
    /// Body: waterVolumeL (bắt buộc) + độ mặn hiện tại và mong muốn + saltType/purity.
    /// </summary>
    [HttpPost("salinity")]
    public IActionResult MixSalinity([FromBody] SalinityMixRequest req)
    {
        try
        {
            return Ok(ApiResponse<SalinityMixResult>.Ok(
                SalinityMixingCalculator.Calculate(req)));
        }
        catch (ArgumentException ex)
        {
            // Thể tích ≤ 0, độ mặn âm, độ tinh khiết ngoài (0;100], mục tiêu 0‰ ⇒ 400.
            throw AppException.BadRequest(ex.Message);
        }
    }
}
