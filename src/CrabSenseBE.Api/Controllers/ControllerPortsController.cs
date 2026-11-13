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
        var now = DateTime.UtcNow;
        var existing = await _db.ControllerPorts.Where(p => p.DeviceId == id).ToDictionaryAsync(p => p.PortId, ct);
        // Bound retained history as well as each individual report.
        if (existing.Count + ports.Count(p => !existing.ContainsKey(p.PortId)) > 256)
            return BadRequest(new { message = "Controller port history limit reached." });
        if (header is not null && report.Revision == header.Revision)
        {
            header.ReportedAt = now;
            foreach (var p in ports)
                if (existing.TryGetValue(p.PortId, out var row) && p.Present)
                    row.LastSeenAt = now;
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Conflict(new { message = "Concurrent inventory report; retry." }); }
            return Ok(new { success = true, revision = header.Revision, changed = false });
        }
        if (header is null)
        {
            header = new ControllerPortInventory { DeviceId = id };
            _db.ControllerPortInventories.Add(header);
        }
        header.Revision = report.Revision;
        header.ManifestHash = hash;
        header.ReportedAt = now;
        // Keep missing ports for diagnostics; do not delete RAS mappings or actuators.
        foreach (var p in existing.Values) p.Present = false;
        foreach (var p in ports)
        {
            if (!existing.TryGetValue(p.PortId, out var row))
            {
                row = new ControllerPort { DeviceId = id, PortId = p.PortId };
                _db.ControllerPorts.Add(row);
            }
            if (!row.Present || row.Channel != p.Channel || row.Gpio != p.Gpio || row.HardwareUid != p.HardwareUid)
            {
                row.ObservedOn = null;
                row.LastObservedAt = null;
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

    public sealed record RelayObservation(string Channel, bool On);
    public sealed record ObservationReport(long Revision, IReadOnlyList<RelayObservation>? Outputs);

    [HttpPut("{deviceCode}/ports/observations")]
    [AllowAnonymous]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Observe(string deviceCode, [FromBody] ObservationReport report, CancellationToken ct)
    {
        var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(Request, _configuration, _uow, deviceCode, ct);
        if (denied is not null) return denied;
        if (identity!.IsLegacyCredential) return Unauthorized(new { message = "Dedicated device credential required." });
        if (report.Outputs is null || report.Outputs.Count is < 1 or > 64
            || report.Outputs.Any(x => x is null || !Token(x.Channel, 32))
            || report.Outputs.Select(x => x.Channel).Distinct(StringComparer.Ordinal).Count() != report.Outputs.Count)
            return BadRequest(new { message = "Invalid observations." });
        var id = identity.DeviceId;
        var inventory = await _db.ControllerPortInventories.SingleOrDefaultAsync(x => x.DeviceId == id, ct);
        if (inventory is null || inventory.Revision != report.Revision)
            return Conflict(new { message = "Inventory revision mismatch." });
        var rows = await _db.ControllerPorts.Where(x => x.DeviceId == id && x.Present && x.Direction == "output"
            && (x.Kind == "ssr" || x.Kind == "relay")).ToListAsync(ct);
        if (rows.Count != report.Outputs.Count || rows.Any(x => x.Channel is null)
            || !rows.Select(x => x.Channel!).ToHashSet(StringComparer.Ordinal).SetEquals(report.Outputs.Select(x => x.Channel)))
            return BadRequest(new { message = "Observation must match the current output inventory." });
        var values = report.Outputs.ToDictionary(x => x.Channel, x => x.On, StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.ObservedOn = values[row.Channel!];
            row.LastObservedAt = now;
        }
        // Force the inventory concurrency token into the transaction so a
        // concurrently replaced manifest cannot accept stale observations.
        _db.Entry(inventory).Property(x => x.ReportedAt).IsModified = true;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { message = "Inventory changed; retry." }); }
        return Ok(new { success = true });
    }

    [HttpGet("{id:guid}/ports")]
    [Authorize(Roles = AppRoles.FarmPortal + "," + AppRoles.Platform)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        // Reuse existing farm scope and device read authorization.
        await _iot.GetDeviceAsync(id, ct);
        var inventory = await _db.ControllerPortInventories.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id, ct);
        var rows = await _db.ControllerPorts.AsNoTracking().Where(p => p.DeviceId == id).OrderBy(p => p.PortId).ToListAsync(ct);
        var components = await _db.RasComponents.AsNoTracking().Where(x => x.RelayDeviceId == id)
            .Select(x => new { x.Id, x.Code, x.Name, x.RelayChannel }).ToListAsync(ct);
        var ports = rows.Select(p => new
        {
            p.DeviceId, p.PortId, p.Channel, p.Gpio, p.Kind, p.Direction, p.Discovery,
            p.HardwareUid, p.Present, p.RemoteControl, p.LastSeenAt, p.ObservedOn, p.LastObservedAt,
            // Existing RAS bindings are read-only here; no inferred equipment assignment.
            bindings = components.Where(x => p.Channel != null && x.RelayChannel == p.Channel).ToList()
        });
        return Ok(new { success = true, data = new { revision = inventory?.Revision, reportedAt = inventory?.ReportedAt, ports } });
    }

    private static bool Token(string? s, int max) => s is not null && s.Length > 0 && s.Length <= max
        && Regex.IsMatch(s, @"\A[A-Za-z0-9_.:-]+\z", RegexOptions.CultureInvariant);
}
