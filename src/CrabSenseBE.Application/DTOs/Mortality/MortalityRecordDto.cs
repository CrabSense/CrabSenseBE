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

    Guid RecordedBy
);