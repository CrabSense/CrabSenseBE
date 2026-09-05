using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>CRUD + lọc: Khu nuôi / Dãy / Crab Farm Box (+ Crab cơ bản).</summary>
public interface IFarmingService
{
    // ─── FarmingArea ───────────────────────────────────────────────────────
    Task<ApiResponse<PagedResult<FarmingAreaDto>>> GetAreasAsync(FarmingAreaFilter filter, CancellationToken ct = default);
    Task<ApiResponse<FarmingAreaDto>> GetAreaByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<NextFarmCodeDto>> GetNextAreaCodeAsync(CancellationToken ct = default);
    Task<ApiResponse<FarmingAreaDto>> CreateAreaAsync(CreateFarmingAreaRequest request, Guid ownerUserId, CancellationToken ct = default);
    Task<ApiResponse<FarmingAreaDto>> UpdateAreaAsync(Guid id, UpdateFarmingAreaRequest request, CancellationToken ct = default);
    Task<ApiResponse<FarmAvatarDto>> UploadAvatarAsync(
        Guid? areaId, Stream data, string fileName, string contentType, Guid? uploadedBy, CancellationToken ct = default);
    Task<ApiResponse> DeleteAreaAsync(Guid id, CancellationToken ct = default);

    // ─── FarmingRow ────────────────────────────────────────────────────────
    Task<ApiResponse<PagedResult<FarmingRowDto>>> GetRowsAsync(FarmingRowFilter filter, CancellationToken ct = default);
    Task<ApiResponse<FarmingRowDto>> GetRowByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<NextRowCodeDto>> GetNextRowCodeAsync(CancellationToken ct = default);
    Task<ApiResponse<FarmingRowDto>> CreateRowAsync(CreateFarmingRowRequest request, CancellationToken ct = default);
    Task<ApiResponse<FarmingRowDto>> UpdateRowAsync(Guid id, UpdateFarmingRowRequest request, CancellationToken ct = default);
    Task<ApiResponse> DeleteRowAsync(Guid id, CancellationToken ct = default);

    // ─── Box ───────────────────────────────────────────────────────────────
    Task<ApiResponse<PagedResult<BoxDto>>> GetBoxesAsync(BoxFilter filter, CancellationToken ct = default);
    Task<ApiResponse<BoxDto>> GetBoxByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<FarmAvailabilityDto>> GetAvailabilityAsync(Guid? farmingAreaId, Guid? farmingRowId, CancellationToken ct = default);
    Task<ApiResponse<BoxDto>> CreateBoxAsync(CreateBoxRequest request, CancellationToken ct = default);
    Task<ApiResponse<BoxDto>> UpdateBoxAsync(Guid id, UpdateBoxRequest request, CancellationToken ct = default);
    Task<ApiResponse<BoxDto>> UpdateBoxStatusAsync(Guid boxId, UpdateBoxStatusRequest request, CancellationToken ct = default);
    Task<ApiResponse> DeleteBoxAsync(Guid id, CancellationToken ct = default);

    // ─── Crab ──────────────────────────────────────────────────────────────
    Task<ApiResponse<PagedResult<CrabDto>>> GetCrabsAsync(int page, int pageSize, Guid? farmingAreaId = null, CancellationToken ct = default);
    Task<ApiResponse<CrabDto>> GetCrabByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<NextCrabCodeDto>> GetNextCrabCodeAsync(CancellationToken ct = default);
    Task<ApiResponse<CrabDto>> CreateCrabAsync(CreateCrabRequest request, CancellationToken ct = default);
    Task<ApiResponse<CrabDto>> UpdateCrabAsync(Guid id, UpdateCrabRequest request, CancellationToken ct = default);
    /// <summary>Soft-delete: IsAlive=false, free box, keep history.</summary>
    Task<ApiResponse> DeleteCrabAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<CrabStatusHistoryDto>>> GetCrabStatusHistoryAsync(Guid crabId, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<CrabWeightHistoryDto>>> GetCrabWeightHistoryAsync(Guid crabId, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<CrabAiAnalysisDto>>> GetCrabAiAnalysesAsync(Guid crabId, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<CrabHarvestHistoryDto>>> GetCrabHarvestHistoryAsync(Guid crabId, CancellationToken ct = default);
}
