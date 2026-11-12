using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

internal static class MediaReferenceGuard
{
    internal static string ContentUrl(Guid id) => $"/api/media/{id:D}/content";

    internal static bool TryParseContentId(string? url, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;
        var path = Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https" ? absolute.AbsolutePath : url;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 && parts[0] == "api" && parts[1] == "media"
            && parts[3] == "content" && Guid.TryParse(parts[2], out id) && id != Guid.Empty;
    }

    // Bind newly uploaded private pending images in the same unit of work as
    // crab registration/update. An arbitrary caller cannot claim another user's upload.
    internal static async Task BindCrabAsync(IUnitOfWork uow, FarmAreaScope areas,
        Crab crab, IEnumerable<string> urls, CancellationToken ct)
    {
        foreach (var url in urls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParseContentId(url, out var id)) continue;
            var asset = await uow.MediaAssets.GetByIdAsync(id, ct)
                ?? throw AppException.NotFound("MediaAsset");
            if (asset.Category != "image")
                throw AppException.BadRequest("Media không phải ảnh.");
            if (asset.CrabId == crab.Id && asset.RelatedEntityType == "crab") continue;
            var actor = await areas.CurrentUserAsync(ct);
            if (asset.RelatedEntityType != "crab-pending" || asset.RelatedEntityId.HasValue
                || asset.CrabId.HasValue || actor is null || asset.UploadedBy != actor.Id)
                throw AppException.Forbidden("Không thể gán ảnh của người dùng hoặc cua khác.");
            asset.CrabId = crab.Id;
            asset.BoxId = crab.BoxId;
            asset.RelatedEntityType = "crab";
            asset.RelatedEntityId = crab.Id;
            uow.MediaAssets.Update(asset);
        }
    }
}
