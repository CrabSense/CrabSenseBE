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
    public UserRole Role { get; set; } = UserRole.Staff;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }

    // Navigation
    public ICollection<FarmingArea> OwnedAreas { get; set; } = new List<FarmingArea>();
    public ICollection<OperationLog> OperationLogs { get; set; } = new List<OperationLog>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
