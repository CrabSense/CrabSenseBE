using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/controllers")]
public sealed class ControllerPortsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly IConfiguration _configuration;
    private readonly IIotService _iot;
    public ControllerPortsController(AppDbContext db, IUnitOfWork uow, IConfiguration configuration, IIotService iot)
        => (_db, _uow, _configuration, _iot) = (db, uow, configuration, iot);

    public sealed record PortReport(string PortId, string? Channel, int? Gpio, string Kind,
        string Direction, string Discovery, string? HardwareUid, bool Present, bool RemoteControl);
    public sealed record ManifestReport(int SchemaVersion, long Revision, IReadOnlyList<PortReport>? Ports);

    // Device credential, not a shared kiosk credential, is required to change the inventory.
    [HttpPut("{deviceCode}/ports/manifest")]
    [AllowAnonymous]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Report(string deviceCode, [FromBody] ManifestReport report, CancellationToken ct)
    {
        var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(Request, _configuration, _uow, deviceCode, ct);
        if (denied is not null) return denied;
        if (identity!.IsLegacyCredential) return Unauthorized(new { message = "Dedicated device credential required." });
        if (report.SchemaVersion != 1 || report.Revision < 1 || report.Ports is null || report.Ports.Count > 64)
            return BadRequest(new { message = "Invalid manifest schema, revision or port count." });
        var ports = report.Ports.OrderBy(p => p?.PortId, StringComparer.Ordinal).ToArray();
        if (ports.Any(p => p is null || !Token(p.PortId, 64) || !Token(p.Kind, 40)
            || p.Direction is not ("input" or "output" or "bidirectional")
            || p.Discovery is not ("declared" or "detected")
            || (p.Channel is not null && !Token(p.Channel, 32))
            || (p.Gpio is int gpio && (gpio < 0 || gpio > 63))
            || (p.HardwareUid is not null && !Token(p.HardwareUid, 100))
            || (p.Discovery == "detected" && string.IsNullOrEmpty(p.HardwareUid))
            || (p.RemoteControl && (p.Direction != "output" || p.Channel is null))))
            return BadRequest(new { message = "Invalid port descriptor." });
        if (ports.Select(p => p.PortId).Distinct(StringComparer.Ordinal).Count() != ports.Length
            || ports.Where(p => p.Channel is not null).GroupBy(p => p.Channel, StringComparer.Ordinal).Any(g => g.Count() > 1)
            || ports.Where(p => p.Gpio.HasValue).GroupBy(p => p.Gpio).Any(g => g.Count() > 1))
            return BadRequest(new { message = "Duplicate port, channel or GPIO." });

        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(ports)));
        var id = identity.DeviceId;
        var header = await _db.ControllerPortInventories.SingleOrDefaultAsync(x => x.DeviceId == id, ct);
        if (header is not null && (report.Revision < header.Revision
            || (report.Revision == header.Revision && hash != header.ManifestHash)))
            return Conflict(new { message = "Stale or conflicting revision.", revision = header.Revision });
        if (header is not null && report.Revision == header.Revision)
            return Ok(new { success = true, revision = header.Revision, changed = false });

        var now = DateTime.UtcNow;
        if (header is null)
        {
            header = new ControllerPortInventory { DeviceId = id };
            _db.ControllerPortInventories.Add(header);
        }
        header.Revision = report.Revision;
        header.ManifestHash = hash;
        header.ReportedAt = now;
        var existing = await _db.ControllerPorts.Where(p => p.DeviceId == id).ToDictionaryAsync(p => p.PortId, ct);
        // Keep missing ports for diagnostics; do not delete RAS mappings or actuators.
        foreach (var p in existing.Values) p.Present = false;
        foreach (var p in ports)
        {
            if (!existing.TryGetValue(p.PortId, out var row))
            {
                row = new ControllerPort { DeviceId = id, PortId = p.PortId };
                _db.ControllerPorts.Add(row);
            }
            row.Channel = p.Channel;
            row.Gpio = p.Gpio;
            row.Kind = p.Kind;
            row.Direction = p.Direction;
            row.Discovery = p.Discovery;
            row.HardwareUid = p.HardwareUid;
            row.Present = p.Present;
            row.RemoteControl = p.RemoteControl;
            row.LastSeenAt = now;
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { message = "Concurrent inventory report; retry." }); }
        return Ok(new { success = true, revision = report.Revision, changed = true });
    }

    [HttpGet("{id:guid}/ports")]
    [Authorize(Roles = AppRoles.FarmPortal + "," + AppRoles.Platform)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        // Reuse existing farm scope and device read authorization.
        await _iot.GetDeviceAsync(id, ct);
        var inventory = await _db.ControllerPortInventories.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id, ct);
        var ports = await _db.ControllerPorts.AsNoTracking().Where(p => p.DeviceId == id).OrderBy(p => p.PortId).ToListAsync(ct);
        return Ok(new { success = true, data = new { revision = inventory?.Revision, reportedAt = inventory?.ReportedAt, ports } });
    }

    private static bool Token(string? s, int max) => s is not null && s.Length > 0 && s.Length <= max
        && Regex.IsMatch(s, @"\A[A-Za-z0-9_.:-]+\z", RegexOptions.CultureInvariant);
}
