using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;

namespace CrabSenseBE.Application.Interfaces;

public interface IFarmingService
{
    // FarmingArea
    Task<ApiResponse<IEnumerable<FarmingAreaDto>>> GetAreasAsync(CancellationToken ct = default);
    Task<ApiResponse<FarmingAreaDto>> GetAreaByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<FarmingAreaDto>> CreateAreaAsync(CreateFarmingAreaRequest request, CancellationToken ct = default);
    Task<ApiResponse<FarmingAreaDto>> UpdateAreaAsync(Guid id, UpdateFarmingAreaRequest request, CancellationToken ct = default);
    Task<ApiResponse> DeleteAreaAsync(Guid id, CancellationToken ct = default);

    // FarmingRow
    Task<ApiResponse<IEnumerable<FarmingRowDto>>> GetRowsByAreaAsync(Guid areaId, CancellationToken ct = default);
    Task<ApiResponse<FarmingRowDto>> CreateRowAsync(CreateFarmingRowRequest request, CancellationToken ct = default);

    // Box
    Task<ApiResponse<IEnumerable<BoxDto>>> GetBoxesByRowAsync(Guid rowId, CancellationToken ct = default);
    Task<ApiResponse<BoxDto>> CreateBoxAsync(CreateBoxRequest request, CancellationToken ct = default);

    // Crab
    Task<ApiResponse<PagedResult<CrabDto>>> GetCrabsAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<CrabDto>> GetCrabByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<CrabDto>> CreateCrabAsync(CreateCrabRequest request, CancellationToken ct = default);
    Task<ApiResponse<CrabDto>> UpdateCrabAsync(Guid id, UpdateCrabRequest request, CancellationToken ct = default);
}
