using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>
/// Metadata file media lưu trên Google Drive (hoặc local fallback).
/// category: image | video | log
/// </summary>
public class MediaAsset : BaseEntity
{
    /// <summary>image | video | log</summary>
    public string Category { get; set; } = "image";

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }

    /// <summary>GoogleDrive | Local</summary>
    public string Provider { get; set; } = "GoogleDrive";

    /// <summary>Drive fileId hoặc đường dẫn local.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Link mở trên trình duyệt (Drive webViewLink).</summary>
    public string? WebViewLink { get; set; }

    /// <summary>Link tải trực tiếp (nếu có).</summary>
    public string? WebContentLink { get; set; }

    /// <summary>Link share (anyone with link / shared folder).</summary>
    public string? ShareLink { get; set; }

    public bool IsShared { get; set; }

    public Guid? BoxId { get; set; }
    public Guid? CrabId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? UploadedBy { get; set; }

    /// <summary>box | crab | device | alert | inspection | other</summary>
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }

    public string? Notes { get; set; }

    public Box? Box { get; set; }
    public Crab? Crab { get; set; }
    public Device? Device { get; set; }
    public AppUser? Uploader { get; set; }
}
