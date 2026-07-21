using CrabSenseBE.Application.DTOs.Mortality;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>
/// Service quản lý việc ghi nhận và truy vấn cua chết.
/// </summary>
public interface IMortalityService
{
    /// <summary>
    /// Ghi nhận một cá thể cua chết.
    /// </summary>
    Task<MortalityRecordDto> RecordMortalityAsync(
        Guid recordedBy,
        RecordMortalityRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các bản ghi cua chết.
    /// </summary>
    Task<IReadOnlyList<MortalityRecordDto>>
        GetMortalityRecordsAsync(
            CancellationToken cancellationToken = default);
}