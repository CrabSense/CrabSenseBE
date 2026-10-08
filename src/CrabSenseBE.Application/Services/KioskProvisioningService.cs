using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Kiosk;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public sealed class KioskProvisioningService : IKioskProvisioningService
{
    private const int CodeTtlMinutes = 10;
    private static readonly char[] Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private readonly IUnitOfWork _uow;

    public KioskProvisioningService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<KioskCreatedDto>> CreateAsync(CreateKioskRequest request, CancellationToken ct = default)
    {
        var area = await _uow.FarmingAreas.GetByIdAsync(request.FarmingAreaId, ct)
            ?? throw AppException.NotFound("FarmingArea");

        var kiosk = new FarmKiosk
        {
            Code = await NextCodeAsync(ct),
            FarmingAreaId = area.Id,
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            Status = "Created"
        };
        await _uow.FarmKiosks.AddAsync(kiosk, ct);
        var issued = Issue(kiosk.Id);
        await _uow.KioskProvisioningCodes.AddAsync(issued, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<KioskCreatedDto>.Ok(MapCreated(kiosk, area.Code, issued), "Provisioning code issued.");
    }

    public async Task<ApiResponse<IReadOnlyList<KioskListItemDto>>> ListAsync(Guid farmingAreaId, CancellationToken ct = default)
    {
        var kiosks = (await _uow.FarmKiosks.FindAsync(k => k.FarmingAreaId == farmingAreaId, ct))
            .OrderBy(k => k.Code)
            .ToList();
        var codes = await _uow.KioskProvisioningCodes.GetAllAsync(ct);
        var now = DateTime.UtcNow;
        var items = kiosks.Select(kiosk =>
        {
            var active = codes
                .Where(c => c.KioskId == kiosk.Id && c.UsedAt == null && c.RevokedAt == null && c.ExpiresAt > now)
                .OrderByDescending(c => c.ExpiresAt)
                .FirstOrDefault();
            return MapItem(kiosk, active);
        }).ToList();
        return ApiResponse<IReadOnlyList<KioskListItemDto>>.Ok(items);
    }

    public async Task<ApiResponse<KioskCreatedDto>> IssueCodeAsync(Guid kioskId, CancellationToken ct = default)
    {
        var kiosk = await _uow.FarmKiosks.GetByIdAsync(kioskId, ct)
            ?? throw AppException.NotFound("Kiosk");
        if (kiosk.Status == "Revoked")
            throw AppException.Conflict("Kiosk is revoked.");

        var area = await _uow.FarmingAreas.GetByIdAsync(kiosk.FarmingAreaId, ct)
            ?? throw AppException.NotFound("FarmingArea");
        await RevokeOpenCodesAsync(kiosk.Id, ct);
        var issued = Issue(kiosk.Id);
        await _uow.KioskProvisioningCodes.AddAsync(issued, ct);
        if (kiosk.Status is "Created" or "Offline")
            kiosk.Status = "Provisioning";
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<KioskCreatedDto>.Ok(MapCreated(kiosk, area.Code, issued), "Provisioning code issued.");
    }

    public async Task<ApiResponse<RedeemKioskDto>> RedeemAsync(RedeemKioskRequest request, CancellationToken ct = default)
    {
        var code = request.Code?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            throw AppException.BadRequest("Provisioning code is required.");

        var now = DateTime.UtcNow;
        var match = (await _uow.KioskProvisioningCodes.FindAsync(c => c.Code == code, ct))
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault()
            ?? throw AppException.NotFoundMessage("Provisioning code not found.");
        if (match.UsedAt != null || match.RevokedAt != null || match.ExpiresAt <= now)
            throw AppException.Conflict("Provisioning code is used, revoked, or expired.");

        var kiosk = await _uow.FarmKiosks.GetByIdAsync(match.KioskId, ct)
            ?? throw AppException.NotFound("Kiosk");
        if (kiosk.Status == "Revoked")
            throw AppException.Conflict("Kiosk is revoked.");
        var area = await _uow.FarmingAreas.GetByIdAsync(kiosk.FarmingAreaId, ct)
            ?? throw AppException.NotFound("FarmingArea");

        match.UsedAt = now;
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        foreach (var old in await _uow.KioskCredentials.FindAsync(
                     c => c.KioskId == kiosk.Id && c.RevokedAt == null, ct))
            old.RevokedAt = now;
        await _uow.KioskCredentials.AddAsync(new KioskCredential
        {
            KioskId = kiosk.Id,
            SecretHash = Hash(secret)
        }, ct);

        kiosk.Status = "Online";
        kiosk.LastSeenAt = now;
        if (!string.IsNullOrWhiteSpace(request.LanIp))
            kiosk.LanIp = request.LanIp.Trim();
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<RedeemKioskDto>.Ok(
            new RedeemKioskDto(kiosk.Id, kiosk.Code, area.Code, secret),
            "Provisioning accepted.");
    }

    public async Task<ApiResponse<KioskSessionDto>> TouchAsync(string secret, string? lanIp, CancellationToken ct = default)
    {
        var credential = await FindCredentialAsync(secret, ct);
        var kiosk = await _uow.FarmKiosks.GetByIdAsync(credential.KioskId, ct)
            ?? throw AppException.NotFound("Kiosk");
        if (kiosk.Status == "Revoked")
            throw AppException.Forbidden("Kiosk is revoked.");
        var area = await _uow.FarmingAreas.GetByIdAsync(kiosk.FarmingAreaId, ct)
            ?? throw AppException.NotFound("FarmingArea");

        kiosk.Status = "Online";
        kiosk.LastSeenAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(lanIp))
            kiosk.LanIp = lanIp.Trim();
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<KioskSessionDto>.Ok(
            new KioskSessionDto(kiosk.Id, kiosk.Code, area.Code, kiosk.Status));
    }

    public async Task<ApiResponse<KioskListItemDto>> RevokeAsync(Guid kioskId, CancellationToken ct = default)
    {
        var kiosk = await _uow.FarmKiosks.GetByIdAsync(kioskId, ct)
            ?? throw AppException.NotFound("Kiosk");
        var now = DateTime.UtcNow;
        kiosk.Status = "Revoked";
        await RevokeOpenCodesAsync(kiosk.Id, ct);
        foreach (var credential in await _uow.KioskCredentials.FindAsync(
                     c => c.KioskId == kiosk.Id && c.RevokedAt == null, ct))
            credential.RevokedAt = now;
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<KioskListItemDto>.Ok(MapItem(kiosk, null), "Kiosk revoked.");
    }

    private async Task<KioskCredential> FindCredentialAsync(string secret, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw AppException.Unauthorized();
        var hash = Hash(secret.Trim());
        return await _uow.KioskCredentials.FirstOrDefaultAsync(
                c => c.SecretHash == hash && c.RevokedAt == null, ct)
            ?? throw AppException.Unauthorized();
    }

    private async Task RevokeOpenCodesAsync(Guid kioskId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var open = await _uow.KioskProvisioningCodes.FindAsync(
            c => c.KioskId == kioskId && c.UsedAt == null && c.RevokedAt == null, ct);
        foreach (var code in open)
            code.RevokedAt = now;
    }

    private async Task<string> NextCodeAsync(CancellationToken ct)
    {
        var codes = await _uow.FarmKiosks.GetAllAsync(ct);
        var max = 0;
        foreach (var code in codes.Select(k => k.Code))
        {
            if (code.StartsWith("KIOSK-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(code.AsSpan(6), out var n))
                max = Math.Max(max, n);
        }
        return $"KIOSK-{(max + 1):000}";
    }

    private static KioskProvisioningCode Issue(Guid kioskId) => new()
    {
        KioskId = kioskId,
        Code = NewCode(),
        ExpiresAt = DateTime.UtcNow.AddMinutes(CodeTtlMinutes)
    };

    private static string NewCode()
    {
        Span<char> raw = stackalloc char[8];
        for (var i = 0; i < raw.Length; i++)
            raw[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return $"{new string(raw[..4])}-{new string(raw[4..])}";
    }

    private static string Hash(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(bytes);
    }

    private static KioskCreatedDto MapCreated(FarmKiosk kiosk, string siteCode, KioskProvisioningCode code) =>
        new(kiosk.Id, kiosk.Code, siteCode, kiosk.Status, code.Code, code.ExpiresAt);

    private static KioskListItemDto MapItem(FarmKiosk kiosk, KioskProvisioningCode? code) =>
        new(kiosk.Id, kiosk.Code, kiosk.Name, kiosk.Status, kiosk.LanIp, kiosk.LastSeenAt,
            code?.Code, code?.ExpiresAt);
}
