using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Media;

namespace CrabSenseBE.Application.Interfaces;

public record CrabImageFile(Stream Data, string FileName, string ContentType);

public record CrabImageContent(Stream Data, string ContentType, string FileName);

/// <summary>Upload crab photos to S3 (or fallback media) and optionally attach to a crab.</summary>
public interface ICrabImageService
{
    /// <summary>
    /// Upload 1..n images. If <paramref name="crabId"/> is set, append URLs to that crab.
    /// Without crabId, just returns S3 URLs for the client to send on create.
    /// </summary>
    Task<ApiResponse<IReadOnlyList<CrabImageDto>>> UploadAsync(
        IReadOnlyList<CrabImageFile> files,
        Guid? crabId,
        Guid? uploadedBy,
        CancellationToken ct = default);

    /// <summary>Stream one stored photo (S3/local) by index. Bucket may be private.</summary>
    Task<CrabImageContent?> GetPhotoAsync(Guid crabId, int index, CancellationToken ct = default);
}
