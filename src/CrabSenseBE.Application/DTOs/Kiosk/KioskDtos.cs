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

public record KioskResetDto(int KiosksRemoved, int ControllersUnlinked);

public record RegisterControllerRequest(
    string? DeviceCode,
    string? Mac,
    string? Firmware,
    string? Hardware,
    string? IpAddress);

public record RegisterControllerDto(
    Guid DeviceId,
    string DeviceCode,
    string EdgeState,
    string? Secret);

public record EdgeControllerDto(
    Guid Id,
    string DeviceCode,
    string? Mac,
    string? IpAddress,
    string EdgeState,
    string LinkStatus,
    Guid? KioskId,
    string? KioskCode,
    DateTime? LastSeenAt);
