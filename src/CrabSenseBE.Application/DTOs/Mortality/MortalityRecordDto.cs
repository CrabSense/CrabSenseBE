using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Application.DTOs.Mortality;

/// <summary>
/// Thông tin bản ghi cua chết trả về cho client.
/// </summary>
public record MortalityRecordDto
(
    Guid Id,

    Guid CrabId,

    string? CrabTag,

    Guid BoxId,

    string BoxCode,

    Guid FarmingRowId,

    string RowName,

    Guid FarmingAreaId,

    string AreaName,

    DateTime MortalityDate,

    MortalityCause Cause,

    string? Notes,

    Guid RecordedBy,

    /// <summary>Trọng lượng lúc chết (g) — snapshot, null nếu không xác định được.</summary>
    decimal? WeightGram = null,

    decimal? CarapaceWidthMm = null,

    decimal? CarapaceLengthMm = null
);