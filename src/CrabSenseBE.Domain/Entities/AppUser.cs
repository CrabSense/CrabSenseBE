using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Người dùng hệ thống — MOD-AUTH</summary>
public class AppUser : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    /// <summary>Mã tài khoản theo role: SAD/FAD/FMN/FST-00001. Hệ thống sinh, không sửa từ client.</summary>
    public string? UserCode { get; set; }
    public string? AvatarUrl { get; set; }
    /// <summary>JSON prefs cho thông báo cá nhân (Account tab).</summary>
    public string? NotificationPrefsJson { get; set; }
    public UserRole Role { get; set; } = UserRole.FarmStaff;
    /// <summary>Các khu/trang trại được gán cho FarmAdministrator; quan hệ nhiều-nhiều.</summary>
    public ICollection<FarmingArea> ManagedAreas { get; set; } = new List<FarmingArea>();
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public string? LastUserAgent { get; set; }
    /// <summary>
    /// Legacy plaintext token retained only so existing sessions can rotate
    /// once after deployment. New tokens are never stored here.
    /// </summary>
    public string? RefreshToken { get; set; }
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }
    /// <summary>Optimistic-concurrency generation for refresh rotation/revocation.</summary>
    public int RefreshTokenVersion { get; set; }
    /// <summary>Tăng để thu hồi ngay mọi access token đã phát hành trước đó.</summary>
    public int TokenVersion { get; set; }

    // Navigation
    public ICollection<OperationLog> OperationLogs { get; set; } = new List<OperationLog>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
