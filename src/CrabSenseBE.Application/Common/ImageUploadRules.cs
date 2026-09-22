using CrabSenseBE.Application.Interfaces;

namespace CrabSenseBE.Application.Common;

public static class ImageUploadRules
{
    public static readonly HashSet<string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif", "image/heic", "image/heif"
    };

    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".heic", ".heif"
    };

    public const int MaxFiles = 10;
    public const long MaxBytesPerFile = 10 * 1024 * 1024;
    public const int MaxUrls = 20;

    public static void ValidateBatch(IReadOnlyList<CrabImageFile> files)
    {
        if (files is null || files.Count == 0)
            throw AppException.BadRequest("At least one image file is required.");
        if (files.Count > MaxFiles)
            throw AppException.BadRequest($"Maximum {MaxFiles} images per upload.");
        foreach (var file in files)
            ValidateFile(file);
    }

    public static void ValidateFile(CrabImageFile file)
    {
        if (file.Data is null)
            throw AppException.BadRequest("File stream is required.");
        if (string.IsNullOrWhiteSpace(file.FileName))
            throw AppException.BadRequest("File name is required.");

        var ext = Path.GetExtension(file.FileName);
        var typeOk = !string.IsNullOrWhiteSpace(file.ContentType) && ContentTypes.Contains(file.ContentType);
        var extOk = !string.IsNullOrWhiteSpace(ext) && Extensions.Contains(ext);
        if (!typeOk && !extOk)
            throw AppException.BadRequest($"Unsupported image type '{file.ContentType}'. Use jpeg, png, webp, gif, or heic.");

        if (file.Data.CanSeek && file.Data.Length > MaxBytesPerFile)
            throw AppException.BadRequest($"Each image must be <= {MaxBytesPerFile / (1024 * 1024)} MB.");
    }
}
