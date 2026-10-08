namespace CrabSenseBE.Application.DTOs.Kiosk;

public record CreateKioskRequest(Guid FarmingAreaId, string? Name);

public record KioskCreatedDto(
    Guid Id,
    string Code,
    string SiteCode,
    string Status,
    string ProvisioningCode,
    DateTime ExpiresAt);

public record KioskListItemDto(
    Guid Id,
    string Code,
    string? Name,
    string Status,
    string? LanIp,
    DateTime? LastSeenAt,
    string? ProvisioningCode,
    DateTime? ProvisioningExpiresAt);

public record RedeemKioskRequest(string Code, string? LanIp);

public record RedeemKioskDto(
    Guid KioskId,
    string KioskCode,
    string SiteCode,
    string Secret);

public record KioskSessionDto(
    Guid KioskId,
    string KioskCode,
    string SiteCode,
    string Status);
