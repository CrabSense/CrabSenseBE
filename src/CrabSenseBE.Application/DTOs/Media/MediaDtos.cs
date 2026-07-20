namespace CrabSenseBE.Application.DTOs.Media;

public record MediaAssetDto(
    Guid Id,
    string Category,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Provider,
    string StorageKey,
    string? WebViewLink,
    string? ShareLink,
    bool IsShared,
    Guid? BoxId,
    Guid? CrabId,
    Guid? DeviceId,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    DateTime CreatedAt);

public record MediaUploadMeta(
    string Category,
    Guid? BoxId,
    Guid? CrabId,
    Guid? DeviceId,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    string? Notes,
    bool SharePublic = true);

public record MediaShareResultDto(Guid Id, string ShareLink, bool IsShared);
