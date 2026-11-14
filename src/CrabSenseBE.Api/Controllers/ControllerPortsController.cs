using System.Security.Cryptography;
using System.Text.Json;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

public record PortManifestItem(string PortCode, string Type, int? Channel, int Gpio, int? SecondaryGpio = null,
    string? ActiveLevel = null, string? SensorCode = null, bool? On = null);
public record ControllerManifest(string DeviceCode, string HardwareModel, string FirmwareVersion, int ConfigVersion,
    IReadOnlyList<PortManifestItem> Ports);
public record BindSensorPortRequest(Guid? SensorId);
public record PortInventoryItem(Guid Id, string PortCode, string Type, int? Channel, int Gpio, int? SecondaryGpio,
    string? ActiveLevel, string? SensorCode, int ConfigVersion, bool IsPresent, bool? ReportedOn,
    string CommissioningStatus, DateTime LastReportedAt);

[ApiController]
[Route("api/iot/controllers")]
public sealed class ControllerPortsController(IUnitOfWork uow, IConfiguration configuration, ICurrentUserService currentUser) : ControllerBase
{
    [HttpPost("capabilities")]
    [AllowAnonymous]
    [RequestSizeLimit(32768)]
    public async Task<IActionResult> Discover([FromBody] ControllerManifest req, CancellationToken ct)
    {
        var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(Request, configuration, uow, req.DeviceCode, ct);
        if (denied is not null) return denied;
        if (identity!.IsLegacyCredential || Request.Headers["X-Edge-Protocol"].ToString() != "2")
            return Unauthorized(new { success = false, message = "Device-specific Edge V2 credential required." });
        var device = await uow.Devices.GetByIdAsync(identity.DeviceId, ct) ?? throw AppException.NotFound("Controller");
        var ports = Validate(req);
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            req.HardwareModel, ports = ports.Select(p => p with { On = null })
        })));
        if (req.ConfigVersion < device.CapabilityVersion ||
            req.ConfigVersion == device.CapabilityVersion && device.CapabilityHash != hash)
            throw AppException.Conflict("Stale or changed capability layout; increment configVersion.");
        var existing = (await uow.ControllerPorts.FindAsync(p => p.DeviceId == device.Id, ct)).ToList();
        var now = DateTime.UtcNow;
        if (existing.Count + ports.Count(p => !existing.Any(x => x.PortCode == p.PortCode)) > 256)
            throw AppException.BadRequest("Controller port history limit reached.");
        foreach (var old in existing.Where(p => !ports.Any(x => x.PortCode == p.PortCode)))
        {
            old.IsPresent = false; old.ReportedOn = null; old.CommissioningStatus = "Disabled";
            uow.ControllerPorts.Update(old);
        }
        foreach (var item in ports)
        {
            var port = existing.SingleOrDefault(p => p.PortCode == item.PortCode);
            var fresh = port is null;
            port ??= new ControllerPort { DeviceId = device.Id, PortCode = item.PortCode };
            // A newly added channel must not reset commissioning of unchanged
            // existing outputs; a changed wiring descriptor always does.
            if (fresh || !port.IsPresent || port.PortType != item.Type || port.Channel != item.Channel
                || port.Gpio != item.Gpio || port.SecondaryGpio != item.SecondaryGpio
                || port.ActiveLevel != item.ActiveLevel || port.SensorCode != item.SensorCode)
                port.CommissioningStatus = "Pending";
            port.PortType = item.Type; port.Channel = item.Channel; port.Gpio = item.Gpio;
            port.SecondaryGpio = item.SecondaryGpio; port.ActiveLevel = item.ActiveLevel; port.SensorCode = item.SensorCode;
            port.ConfigVersion = req.ConfigVersion; port.IsPresent = true; port.ReportedOn = item.On; port.LastReportedAt = now;
            if (fresh) { await uow.ControllerPorts.AddAsync(port, ct); existing.Add(port); }
            else uow.ControllerPorts.Update(port);
        }
        // Match already registered metadata only; passive loads are never invented.
        var sensors = (await uow.Sensors.FindAsync(s => s.DeviceId == device.Id, ct)).ToList();
        foreach (var port in existing.Where(p => p.IsPresent && (p.PortType == "DIGITAL_INPUT" || p.PortType == "ANALOG_INPUT") && p.SensorCode is not null))
        {
            var matches = sensors.Where(s => s.SensorCode == port.SensorCode).ToList();
            if (matches.Count == 1 && matches[0].ControllerPortId is null && matches[0].BusPortId is null && !sensors.Any(s => s.ControllerPortId == port.Id))
            {
                matches[0].ControllerPortId = port.Id;
                matches[0].ConfigurationVersion++; matches[0].LastSeenAt=null; matches[0].AppliedConfigurationVersion=null; matches[0].LastConfigurationAppliedAt=null;
                uow.Sensors.Update(matches[0]);
            }
        }
        var bindings = (await uow.RasComponents.FindAsync(c => c.RelayDeviceId == device.Id, ct)).ToList();
        if (device.CapabilityVersion == 0)
        {
            foreach (var port in existing.Where(p => p.IsPresent && p.PortType == "DIGITAL_OUTPUT" && p.Channel.HasValue))
            {
                var matches = bindings.Where(b => b.RelayChannel == port.Channel!.Value.ToString()).ToList();
                if (matches.Count == 1 && matches[0].ControllerPortId is null && !bindings.Any(b => b.ControllerPortId == port.Id))
                {
                    matches[0].ControllerPortId = port.Id;
                    uow.RasComponents.Update(matches[0]);
                }
            }
        }
        // Store a durable revision snapshot and at most one authenticated heartbeat sample per 5-minute bucket.
        // No credential, secret, or unsigned registration payload is recorded.
        var versionKey = "manifest:" + req.ConfigVersion;
        if (!await uow.ControllerEvents.AnyAsync(x => x.DeviceId == device.Id && x.EventKey == versionKey, ct))
            await uow.ControllerEvents.AddAsync(new ControllerEvent
            {
                DeviceId = device.Id, EventKey = versionKey, Kind = "manifest", InventoryRevision = req.ConfigVersion,
                CreatedAt = now, Summary = $"Accepted manifest revision {req.ConfigVersion}; {ports.Length} current ports",
                DetailsJson = JsonSerializer.Serialize(new { req.HardwareModel, req.FirmwareVersion,
                    req.ConfigVersion, ports = ports.Select(p => p with { On = null }) })
            }, ct);
        var bucket = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute / 5 * 5, 0, DateTimeKind.Utc);
        var sampleKey = "heartbeat:" + bucket.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);
        if (!await uow.ControllerEvents.AnyAsync(x => x.DeviceId == device.Id && x.EventKey == sampleKey, ct))
            await uow.ControllerEvents.AddAsync(new ControllerEvent
            {
                DeviceId = device.Id, EventKey = sampleKey, Kind = "heartbeat", InventoryRevision = req.ConfigVersion,
                CreatedAt = now, Summary = "Authenticated Edge V2 manifest received"
            }, ct);
        device.CapabilityVersion = req.ConfigVersion; device.CapabilityHash = hash;
        device.HardwareModel = req.HardwareModel; device.FirmwareVersion = req.FirmwareVersion;
        device.LastSeenAt = now; device.Status = DeviceStatus.Online;
        uow.Devices.Update(device);
        try { await uow.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw AppException.Conflict("Concurrent capability update; retry the current manifest."); }
        var bound = existing.Where(p => p.IsPresent && p.PortType == "DIGITAL_OUTPUT" && p.Channel.HasValue &&
            p.CommissioningStatus == "Verified" && bindings.Count(c => c.ControllerPortId == p.Id &&
            c.RelayChannel == p.Channel.Value.ToString()) == 1).Select(p => p.Channel!.Value).ToArray();
        return Ok(ApiResponse<object>.Ok(new { configVersion = device.CapabilityVersion, ports = existing.Select(Map),
            boundOutputChannels = bound, hardwareControlDisabled = HardwareControlGuard.IsDisabled(configuration) }));
    }

    public sealed record RelayObservation(int Channel, bool On);
    public sealed record ObservationReport(int ConfigVersion, IReadOnlyList<RelayObservation>? Outputs);

    [HttpPut("{deviceCode}/ports/observations")]
    [AllowAnonymous]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Observe(string deviceCode, [FromBody] ObservationReport req, CancellationToken ct)
    {
        var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(Request, configuration, uow, deviceCode, ct);
        if (denied is not null) return denied;
        if (identity!.IsLegacyCredential || Request.Headers["X-Edge-Protocol"].ToString() != "2")
            return Unauthorized(new { success = false, message = "Device-specific Edge V2 credential required." });
        var device = await uow.Devices.GetByIdAsync(identity.DeviceId, ct) ?? throw AppException.NotFound("Controller");
        if (req.ConfigVersion != device.CapabilityVersion || req.ConfigVersion < 1)
            throw AppException.Conflict("Inventory revision mismatch.");
        if (req.Outputs is null || req.Outputs.Count is < 1 or > 32 ||
            req.Outputs.Any(x => x is null || x.Channel is < 1 or > 32) ||
            req.Outputs.Select(x => x.Channel).Distinct().Count() != req.Outputs.Count)
            throw AppException.BadRequest("Invalid output observations.");
        var rows = (await uow.ControllerPorts.FindAsync(p => p.DeviceId == device.Id && p.IsPresent
            && p.ConfigVersion == req.ConfigVersion && p.PortType == "DIGITAL_OUTPUT", ct)).ToList();
        if (rows.Count != req.Outputs.Count || rows.Any(x => !x.Channel.HasValue)
            || !rows.Select(x => x.Channel!.Value).ToHashSet().SetEquals(req.Outputs.Select(x => x.Channel)))
            throw AppException.BadRequest("Observation must match current output inventory.");
        var states = req.Outputs.ToDictionary(x => x.Channel, x => x.On);
        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.ReportedOn = states[row.Channel!.Value];
            row.LastReportedAt = now;
            uow.ControllerPorts.Update(row);
        }
        // Device capability version is a concurrency token; this update
        // guards against an inventory replacement while saving observations.
        device.LastSeenAt = now;
        device.Status = DeviceStatus.Online;
        uow.Devices.Update(device);
        try { await uow.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw AppException.Conflict("Inventory changed; retry."); }
        return Ok(ApiResponse<object>.Ok(new { configVersion = req.ConfigVersion }));
    }

    [HttpGet("{id:guid}/ports")]
    [Authorize(Roles = AppRoles.FarmPortal + "," + AppRoles.Platform)]
    public async Task<IActionResult> List(Guid id, CancellationToken ct)
    {
        var device = await uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Controller");
        var scope = new FarmAreaScope(uow, currentUser);
        var actor = await scope.CurrentUserAsync(ct) ?? throw AppException.Unauthorized("Authentication required.");
        if (actor.Role != UserRole.SystemAdministrator)
        {
            if (device.FarmingAreaId is not Guid area) throw AppException.Forbidden("Controller has no area.");
            await scope.EnsureReadAsync(area, ct);
        }
        var ports = await uow.ControllerPorts.FindAsync(p => p.DeviceId == id, ct);
        var equipment = await uow.RasComponents.FindAsync(x => x.RelayDeviceId == id, ct);
        var sensors = await uow.Sensors.FindAsync(x => x.DeviceId == id, ct);
        return Ok(ApiResponse<object>.Ok(new { deviceId = id, device.DeviceCode, device.CapabilityVersion,
            device.HardwareModel, hardwareControlDisabled = HardwareControlGuard.IsDisabled(configuration),
            ports = ports.OrderBy(p => p.PortCode).Select(Map),
            equipment = equipment.Select(x => new { x.Id, x.Name, x.RelayChannel, x.ControllerPortId }),
            sensors = sensors.Select(x => new { x.Id, x.SensorCode, x.SensorType, x.ControllerPortId, x.BusPortId, x.BusAddress, x.BusRegister }) }));
    }

    [HttpGet("{id:guid}/history")]
    [Authorize(Roles = AppRoles.Platform)]
    public async Task<IActionResult> History(Guid id, [FromQuery] string? kind = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var actor = await new FarmAreaScope(uow, currentUser).CurrentUserAsync(ct);
        if (actor?.Role != UserRole.SystemAdministrator) return Forbid();
        if (page is < 1 or > 100000 || pageSize is < 1 or > 50 ||
            (kind is not null && kind is not ("manifest" or "heartbeat")) ||
            (from.HasValue && to.HasValue && from.Value >= to.Value))
            throw AppException.BadRequest("Invalid history filters.");
        _ = await uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Controller");
        var query = uow.ControllerEvents.Query().AsNoTracking().Where(x => x.DeviceId == id);
        if (kind is not null) query = query.Where(x => x.Kind == kind);
        if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value.UtcDateTime);
        if (to.HasValue) query = query.Where(x => x.CreatedAt < to.Value.UtcDateTime);
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.Kind, x.InventoryRevision, x.CreatedAt, x.Summary, x.DetailsJson })
            .ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { items, totalCount, page, pageSize }));
    }

    [HttpGet("{id:guid}/alerts")]
    [Authorize(Roles = AppRoles.Platform)]
    public async Task<IActionResult> Alerts(Guid id, CancellationToken ct)
    {
        var actor = await new FarmAreaScope(uow, currentUser).CurrentUserAsync(ct);
        if (actor?.Role != UserRole.SystemAdministrator) return Forbid();
        _ = await uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Controller");
        var source = id.ToString("N");
        var rows = await uow.Alerts.Query().AsNoTracking()
            .Where(x => x.SourceType == "Device" && x.SourceId == source && x.IncidentType == "device_disconnect")
            .OrderByDescending(x => x.CreatedAt).Take(200)
            .Select(x => new { x.Id, x.Message, status = x.Status.ToString(), severity = x.Severity.ToString(),
                x.CreatedAt, x.LastOccurredAt, x.AcknowledgedAt, x.ResolvedAt, x.OccurrenceCount }).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(rows));
    }

    [HttpPost("{id:guid}/alerts/{alertId:guid}/acknowledge")]
    [Authorize(Roles = AppRoles.Platform)]
    public async Task<IActionResult> AcknowledgeAlert(Guid id, Guid alertId, CancellationToken ct)
    {
        var actor = await new FarmAreaScope(uow, currentUser).CurrentUserAsync(ct);
        if (actor?.Role != UserRole.SystemAdministrator) return Forbid();
        _ = await uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Controller");
        var source = id.ToString("N");
        var alert = await uow.Alerts.FirstOrDefaultAsync(x => x.Id == alertId && x.SourceType == "Device" &&
            x.SourceId == source && x.IncidentType == "device_disconnect", ct) ?? throw AppException.NotFound("Controller technical alert");
        if (alert.Status == AlertStatus.Acknowledged)
            return Ok(ApiResponse<object>.Ok(new { alert.Id, status = alert.Status.ToString() }));
        if (alert.Status != AlertStatus.Active) throw AppException.Conflict("Only active alerts may be acknowledged.");
        alert.Status = AlertStatus.Acknowledged;
        alert.AcknowledgedAt = DateTime.UtcNow; alert.AcknowledgedBy = actor.Id;
        await uow.OperationLogs.AddAsync(new OperationLog { UserId = actor.Id, EntityType = "Device",
            EntityId = id, Action = "AcknowledgeControllerAlert", Details = alertId.ToString("N") }, ct);
        await uow.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { alert.Id, status = alert.Status.ToString() }));
    }

    public sealed record CommissioningRequest(int ExpectedConfigVersion, string ExpectedStatus,
        string Status, bool PhysicalCheckConfirmed, string? Note);

    // Commissioning records a technician's physical inspection. It never energizes an output.
    [HttpPut("{id:guid}/ports/{portId:guid}/commissioning")]
    [Authorize(Roles = AppRoles.Platform)]
    public async Task<IActionResult> Commission(Guid id, Guid portId, [FromBody] CommissioningRequest req, CancellationToken ct)
    {
        var actor = await new FarmAreaScope(uow, currentUser).CurrentUserAsync(ct);
        if (actor?.Role != UserRole.SystemAdministrator)
            throw AppException.Forbidden("System Administrator required.");
        if (req.Status is not ("Verified" or "Pending" or "Disabled") || !req.PhysicalCheckConfirmed ||
            string.IsNullOrWhiteSpace(req.Note) || req.Note.Trim().Length > 500)
            throw AppException.BadRequest("A valid status, physical inspection confirmation and note (1–500 characters) are required.");
        var device = await uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Controller");
        var port = await uow.ControllerPorts.GetByIdAsync(portId, ct) ?? throw AppException.NotFound("Port");
        if (port.DeviceId != id || req.ExpectedConfigVersion != device.CapabilityVersion ||
            req.ExpectedConfigVersion != port.ConfigVersion || req.ExpectedStatus != port.CommissioningStatus)
            throw AppException.Conflict("Inventory or commissioning changed; reload before saving.");
        await ControllerPortPolicy.RequireSafeConfigurationAsync(uow, configuration, device, port, ct);
        if (req.Status == "Verified")
        {
            if (string.IsNullOrWhiteSpace(device.CredentialHash) || device.EdgeState is not ("Approved" or "Online")
                || device.Status != DeviceStatus.Online || device.FarmingAreaId is not Guid areaId || !port.Channel.HasValue)
                throw AppException.Conflict("Controller must be authenticated, online and assigned to an area.");
            var bindings = (await uow.RasComponents.FindAsync(x => x.ControllerPortId == port.Id &&
                x.RelayDeviceId == id && x.RelayChannel == port.Channel.Value.ToString(), ct)).ToList();
            if (bindings.Count != 1)
                throw AppException.Conflict("Exactly one current RAS actuator binding is required.");
            var system = await uow.WaterSystems.GetByIdAsync(bindings[0].WaterSystemId, ct);
            if (system?.FarmingAreaId != areaId)
                throw AppException.Conflict("Actuator must belong to the controller area.");
        }
        var before = port.CommissioningStatus;
        port.CommissioningStatus = req.Status;
        await uow.OperationLogs.AddAsync(new OperationLog
        {
            UserId = actor.Id, Action = "ControllerPortCommissioning", EntityType = "Device", EntityId = id,
            Details = $"{port.PortCode}: {before} -> {req.Status}; rev {req.ExpectedConfigVersion}; {req.Note.Trim()}"
        }, ct);
        // Entity returned by the EF repository is tracked; update only the changed status.
        try { await uow.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw AppException.Conflict("Concurrent controller configuration change; reload."); }
        return Ok(ApiResponse<object>.Ok(Map(port)));
    }

    // Link a registered sensor to a discovered input. A bus can serve many
    // devices, but a direct input may only have a single sensor binding.
    [HttpPut("{id:guid}/ports/{portId:guid}/sensor")]
    [Authorize(Roles = AppRoles.Platform)]
    public async Task<IActionResult> BindSensor(Guid id, Guid portId, [FromBody] BindSensorPortRequest req, CancellationToken ct)
    {
        var scope = new FarmAreaScope(uow, currentUser);
        var actor = await scope.CurrentUserAsync(ct);
        if (actor?.Role != UserRole.SystemAdministrator) throw AppException.Forbidden("System Administrator required.");
        var device = await uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Controller");
        var port = await uow.ControllerPorts.GetByIdAsync(portId, ct) ?? throw AppException.NotFound("Port");
        if (port.DeviceId != id || !port.IsPresent || port.ConfigVersion != device.CapabilityVersion ||
            port.PortType is not ("DIGITAL_INPUT" or "ANALOG_INPUT"))
            throw AppException.Conflict("Only a current direct input can be bound here; bus devices require their own addressing.");
        if (req.SensorId is not Guid sensorId) throw AppException.BadRequest("SensorId required.");
        var sensor = await uow.Sensors.GetByIdAsync(sensorId, ct) ?? throw AppException.NotFound("Sensor");
        if (sensor.DeviceId != id || port.SensorCode is not null && !string.Equals(sensor.SensorCode, port.SensorCode, StringComparison.Ordinal))
            throw AppException.Conflict("Sensor is not the registered input of this controller.");
        if (await uow.Sensors.AnyAsync(s => s.Id != sensorId && s.ControllerPortId == portId, ct))
            throw AppException.Conflict("Input already has a sensor.");
        sensor.ControllerPortId = portId;
        sensor.BusPortId = null; sensor.BusAddress = null; sensor.BusRegister = null;
        sensor.LastSeenAt = null; sensor.AppliedConfigurationVersion = null; sensor.LastConfigurationAppliedAt=null; sensor.ConfigurationVersion++;
        await uow.OperationLogs.AddAsync(new OperationLog { UserId=actor.Id, Action="UpdateSensorBinding", EntityType="Sensor", EntityId=sensor.Id },ct);
        uow.Sensors.Update(sensor);
        try { await uow.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw AppException.Conflict("Concurrent input binding; reload inventory."); }
        return Ok(ApiResponse<object>.Ok(new { sensorId, portId }));
    }

    public static PortManifestItem[] Validate(ControllerManifest req)
    {
        if (req.ConfigVersion < 1 || string.IsNullOrWhiteSpace(req.HardwareModel) || req.HardwareModel.Length > 64 ||
            string.IsNullOrWhiteSpace(req.FirmwareVersion) || req.FirmwareVersion.Length > 64 ||
            req.Ports is null || req.Ports.Count is < 1 or > 64) throw AppException.BadRequest("Invalid capability manifest.");
        var codes = new HashSet<string>(StringComparer.Ordinal); var pins = new HashSet<int>();
        var channels = new HashSet<(string,int)>(); var sensorCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in req.Ports)
        {
            if (p is null || string.IsNullOrWhiteSpace(p.PortCode) || p.PortCode.Length > 64 ||
                !System.Text.RegularExpressions.Regex.IsMatch(p.PortCode, "^[A-Z0-9_-]+$") || !codes.Add(p.PortCode) ||
                p.Gpio is < 0 or > 48 || !pins.Add(p.Gpio) ||
                p.SecondaryGpio is int secondary && (secondary is < 0 or > 48 || !pins.Add(secondary)) ||
                p.Type is not ("DIGITAL_OUTPUT" or "DIGITAL_INPUT" or "ANALOG_INPUT" or "RS485" or "I2C" or "UART") ||
                (p.SensorCode is not null && (string.IsNullOrWhiteSpace(p.SensorCode) || p.SensorCode.Length > 128 || !sensorCodes.Add(p.SensorCode)))) throw AppException.BadRequest("Invalid or duplicate port/pin.");
            var digital = p.Type is "DIGITAL_OUTPUT" or "DIGITAL_INPUT";
            if (digital && (p.Channel is not int ch || ch is < 1 or > 32 || !channels.Add((p.Type,ch)) ||
                p.ActiveLevel is not ("HIGH" or "LOW") || p.SecondaryGpio.HasValue) ||
                !digital && (p.Channel.HasValue || p.ActiveLevel is not null) ||
                p.Type is "RS485" or "I2C" or "UART" && !p.SecondaryGpio.HasValue ||
                p.Type != "DIGITAL_OUTPUT" && p.On.HasValue) throw AppException.BadRequest("Invalid port capability.");
        }
        return req.Ports.OrderBy(p=>p.PortCode,StringComparer.Ordinal).ToArray();
    }

    private static PortInventoryItem Map(ControllerPort p) => new(p.Id,p.PortCode,p.PortType,p.Channel,p.Gpio,
        p.SecondaryGpio,p.ActiveLevel,p.SensorCode,p.ConfigVersion,p.IsPresent,p.ReportedOn,p.CommissioningStatus,p.LastReportedAt);
}
