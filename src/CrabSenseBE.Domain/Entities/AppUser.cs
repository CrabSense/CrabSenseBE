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
    public string? EmployeeId { get; set; }
    public string? AvatarUrl { get; set; }
    /// <summary>JSON prefs cho thông báo cá nhân (Account tab).</summary>
    public string? NotificationPrefsJson { get; set; }
    public UserRole Role { get; set; } = UserRole.FarmStaff;
    /// <summary>Khu/trang trại duy nhất được gán cho FarmManager hoặc FarmStaff.</summary>
    public Guid? FarmingAreaId { get; set; }
    public FarmingArea? FarmingArea { get; set; }
    /// <summary>Các khu/trang trại được gán cho FarmAdministrator; quan hệ nhiều-nhiều.</summary>
    public ICollection<FarmingArea> ManagedAreas { get; set; } = new List<FarmingArea>();
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public string? LastUserAgent { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }

    // Navigation
    public ICollection<FarmingArea> OwnedAreas { get; set; } = new List<FarmingArea>();
    public ICollection<OperationLog> OperationLogs { get; set; } = new List<OperationLog>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
