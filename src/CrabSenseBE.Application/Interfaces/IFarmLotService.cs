using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.DTOs.Media;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>CRUD lô cua + vụ nuôi (CropBatch).</summary>
public interface IFarmLotService
{
    Task<ApiResponse<IEnumerable<CrabLotDto>>> GetLotsAsync(CancellationToken ct = default);
    Task<ApiResponse<CrabLotDto>> GetLotByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<NextCrabLotCodeDto>> GetNextLotCodeAsync(DateTime? importDate = null, CancellationToken ct = default);
    Task<ApiResponse<CrabLotDto>> CreateLotAsync(CreateCrabLotRequest req, CancellationToken ct = default);
    Task<ApiResponse<CrabLotDto>> UpdateLotAsync(Guid id, UpdateCrabLotRequest req, CancellationToken ct = default);
    Task<ApiResponse> DeleteLotAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<CrabImageDto>>> UploadImagesAsync(
        Guid lotId,
        IReadOnlyList<CrabImageFile> files,
        Guid? uploadedBy,
        CancellationToken ct = default);
    Task<CrabImageContent?> GetPhotoAsync(Guid lotId, int index, CancellationToken ct = default);

//     Task<ApiResponse<IEnumerable<CropBatchDto>>> GetBatchesAsync(CancellationToken ct = default);
//     Task<ApiResponse<CropBatchDto>> CreateBatchAsync(CreateCropBatchRequest req, CancellationToken ct = default);
//     Task<ApiResponse<CropBatchDto>> UpdateBatchAsync(Guid id, UpdateCropBatchRequest req, CancellationToken ct = default);
//     Task<ApiResponse> DeleteBatchAsync(Guid id, CancellationToken ct = default);
}
