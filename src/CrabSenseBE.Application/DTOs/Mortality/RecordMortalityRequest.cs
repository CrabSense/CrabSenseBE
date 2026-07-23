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
    string? Notes
);