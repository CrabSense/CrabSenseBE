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

public sealed record MediaContentResult(
    Stream Content,
    string ContentType,
    string FileName,
    long Length);

public record MediaUploadMeta(
    string Category,
    Guid? BoxId,
    Guid? CrabId,
    Guid? DeviceId,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    string? Notes,
    bool SharePublic = false);

public record MediaShareResultDto(Guid Id, string ShareLink, bool IsShared);

/// <summary>One uploaded crab image (S3 public URL).</summary>
public record CrabImageDto(
    string Url,
    string StorageKey,
    string FileName,
    string Provider,
    long SizeBytes);
