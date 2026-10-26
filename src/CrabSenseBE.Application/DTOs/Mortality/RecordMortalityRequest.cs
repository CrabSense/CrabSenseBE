using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Application.DTOs.Mortality;

/// <summary>
/// Request dùng để ghi nhận một cá thể cua chết.
/// </summary>
public record RecordMortalityRequest
(
    /// <summary>
    /// ID của cá thể cua chết.
    /// </summary>
    Guid CrabId,

    /// <summary>
    /// Thời điểm ghi nhận cua chết.
    /// Nếu không truyền, hệ thống có thể dùng thời điểm hiện tại.
    /// </summary>
    DateTime? MortalityDate,

    /// <summary>
    /// Nguyên nhân cua chết.
    /// </summary>
    MortalityCause Cause,

    /// <summary>
    /// Ghi chú thêm.
    /// </summary>
    string? Notes,

    /// <summary>
    /// Trọng lượng lúc chết (g). Bỏ trống thì lấy trọng lượng hiện tại của cua.
    /// </summary>
    decimal? WeightGram = null,

    /// <summary>
    /// Bề rộng mai lúc chết (mm). Bỏ trống thì lấy số đo hiện tại của cua.
    /// </summary>
    decimal? CarapaceWidthMm = null,

    /// <summary>
    /// Bề ngang mai lúc chết (mm). Bỏ trống thì lấy số đo hiện tại của cua.
    /// </summary>
    decimal? CarapaceLengthMm = null
);