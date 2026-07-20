using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Media;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>Upload / liệt kê / share ảnh–video–log (Google Drive).</summary>
public interface IMediaService
{
    Task<ApiResponse<MediaAssetDto>> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        MediaUploadMeta meta,
        Guid? uploadedBy,
        CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<MediaAssetDto>>> ListAsync(
        string? category = null,
        Guid? boxId = null,
        Guid? crabId = null,
        CancellationToken ct = default);

    Task<ApiResponse<MediaAssetDto>> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<ApiResponse<MediaShareResultDto>> ShareAsync(Guid id, CancellationToken ct = default);

    Task<ApiResponse> DeleteAsync(Guid id, CancellationToken ct = default);
}
