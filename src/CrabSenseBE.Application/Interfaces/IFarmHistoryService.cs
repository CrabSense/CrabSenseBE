using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.DTOs.Media;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>Lịch sử nuôi (allocation) + lột xác (molting) + timeline hộp.</summary>
public interface IFarmHistoryService
{
    Task<ApiResponse<CrabBoxAllocationDto>> AllocateCrabAsync(AllocateCrabRequest req, CancellationToken ct = default);
    Task<ApiResponse<CrabBoxAllocationDto>> TransferCrabAsync(MobileTransferCrabRequest req, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<CrabBoxAllocationDto>>> GetAllocationsByCrabAsync(
        Guid crabId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<CrabBoxAllocationDto>>> GetAllocationsByBoxAsync(
        Guid boxId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<ApiResponse<CrabBoxAllocationDto>> UpdateAllocationAsync(
        Guid id, UpdateAllocationRequest req, CancellationToken ct = default);
    Task<ApiResponse> DeleteAllocationAsync(Guid id, CancellationToken ct = default);

    Task<ApiResponse<MoltingRecordDto>> CreateMoltingAsync(CreateMoltingRecordRequest req, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<MoltingRecordDto>>> GetMoltingsByCrabAsync(
        Guid crabId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<MoltingRecordDto>>> GetMoltingsByBoxAsync(
        Guid boxId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<ApiResponse<MoltingRecordDto>> UpdateMoltingAsync(
        Guid id, UpdateMoltingRecordRequest req, CancellationToken ct = default);
    Task<ApiResponse> DeleteMoltingAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<CrabImageDto>>> UploadMoltingImagesAsync(
        Guid moltingId, IReadOnlyList<CrabImageFile> files, Guid? uploadedBy, CancellationToken ct = default);
    Task<CrabImageContent?> GetMoltingPhotoAsync(Guid moltingId, int index, CancellationToken ct = default);

    Task<ApiResponse<BoxFarmingStatusDto>> GetBoxFarmingStatusAsync(Guid boxId, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<BoxFarmingStatusDto>>> ListBoxesFarmingStatusAsync(
        Guid? farmingAreaId = null, Guid? farmingRowId = null, string? status = null, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<BoxStatusHistoryDto>>> GetBoxStatusHistoryAsync(
        Guid boxId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<BoxStatusHistoryDayDto>>> GetDailyBoxStatusHistoryAsync(
        int days = 7, CancellationToken ct = default);
    Task<ApiResponse<BoxStatusHistoryDto>> UpdateBoxStatusHistoryAsync(
        Guid id, UpdateBoxStatusHistoryRequest req, CancellationToken ct = default);
    Task<ApiResponse> DeleteBoxStatusHistoryAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<BoxFarmingEventDto>>> GetBoxFarmingTimelineAsync(
        Guid boxId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
}
