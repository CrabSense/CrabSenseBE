using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Water;
using CrabSenseBE.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Tính liều khoáng Ca/Mg cho nước RAS — thuần tính toán, KHÔNG ghi DB.
/// Logic: độ mặn chỉ chọn mục tiêu; liều suy từ chênh lệch (test − mục tiêu).
/// Thiếu Mg thì báo cần đo Mg, không suy từ Ca.
/// </summary>
[ApiController]
[Route("api/mineral-dosing")]
[Authorize]
[Tags("37. Water — Mineral dosing (Ca/Mg)")]
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
}
