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
    /// If <paramref name="harvestVoucherId"/> is set, lưu vào {khu}/ThuHoach/{mã phiếu},
    /// append URL vào phiếu và album của các con cua trong phiếu.
    /// Không có cả hai: chỉ trả URL để client gửi kèm lúc tạo bản ghi.
    /// </summary>
    /// <param name="replace">
    /// true = thay danh sách URL cũ bằng lần upload này (dùng khi sửa ảnh bị lưu sai chỗ).
    /// </param>
    Task<ApiResponse<IReadOnlyList<CrabImageDto>>> UploadAsync(
        IReadOnlyList<CrabImageFile> files,
        Guid? crabId,
        Guid? harvestVoucherId,
        Guid? uploadedBy,
        CancellationToken ct = default,
        bool replace = false);

    /// <summary>Stream one stored photo (S3/local) by index. Bucket may be private.</summary>
    Task<CrabImageContent?> GetPhotoAsync(Guid crabId, int index, CancellationToken ct = default);
}
