using System.Security.Claims;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

public record RegisterSensorFromPort(Guid ControllerId, Guid PortId, string SensorCode, string SensorType,
    string? Unit, string? Name = null, Guid? FarmingAreaId = null, Guid? FarmingRowId = null,
    Guid? WaterSystemId = null, string? BusAddress = null, string? BusRegister = null);
public record SensorBindingRequest(Guid? DeviceId, Guid? PortId, string? BusAddress = null, string? BusRegister = null);
public record SensorConfigurationRequest(int SamplingIntervalSeconds, int DisconnectTimeoutSeconds, int ExpectedVersion);
public record SensorCalibrationRequest(DateTimeOffset PerformedAt, decimal ReferenceValue, decimal ObservedValue,
    string Unit, string Method, string Result, string? Note = null);
public record SensorConfigurationAck(string DeviceCode, int Version);
public record SensorErrorReport(string DeviceCode, string ErrorCode, string? Message);

[ApiController]
[Authorize(Roles = AppRoles.Platform)]
[Route("api/sensors")]
public sealed class SensorManagementController(AppDbContext db, IIotService iot) : ControllerBase
{
    private Guid Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id : throw AppException.Unauthorized();
    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();
    private async Task<Sensor> Sensor(Guid id, CancellationToken ct) => await db.Sensors.FindAsync([id], ct)
        ?? throw AppException.NotFound("Sensor");
    private void Audit(Guid id, string action, string? details = null) => db.OperationLogs.Add(new OperationLog
    { UserId = Actor(), Action = action, EntityType = "Sensor", EntityId = id, IpAddress = Ip(), Details = details });
    private static string Status(Sensor s, Device? d, ControllerPort? p)
    {
        if (!s.IsActive) return "Disabled";
        if (s.DeviceId.HasValue && (d == null || d.Status == DeviceStatus.Maintenance || d.EdgeState == "Revoked")) return "Offline";
        if ((s.ControllerPortId.HasValue || s.BusPortId.HasValue) && (p == null || !p.IsPresent || d == null || p.ConfigVersion != d.CapabilityVersion)) return "Offline";
        if (s.LastSeenAt is null) return "Unknown";
        return s.LastSeenAt < DateTime.UtcNow.AddSeconds(-(s.DisconnectTimeoutSeconds ?? 900)) ? "Offline" : "Online";
    }
    private async Task<IReadOnlyList<object>> Inventory(Guid? deviceId, Guid? areaId, Guid? rowId, CancellationToken ct)
    {
        var sensors = await db.Sensors.AsNoTracking().Where(s => deviceId == null || s.DeviceId == deviceId).ToListAsync(ct);
        var ids = sensors.Select(s => s.Id).ToArray();
        var latest = await db.WaterMeasurements.AsNoTracking().Where(m => ids.Contains(m.SensorId))
            .GroupBy(m => m.SensorId).Select(g => g.OrderByDescending(m => m.MeasuredAt).ThenByDescending(m => m.Id).First()).ToListAsync(ct);
        var values = latest.ToDictionary(m => m.SensorId);
        var devices = await db.Devices.AsNoTracking().ToDictionaryAsync(d => d.Id, ct);
        var ports = await db.ControllerPorts.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var rows = await db.FarmingRows.AsNoTracking().ToDictionaryAsync(r => r.Id, ct);
        var areas = await db.FarmingAreas.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
        var systems = await db.WaterSystems.AsNoTracking().ToDictionaryAsync(w => w.Id, ct);
        var result = new List<object>();
        foreach (var s in sensors.OrderBy(s => s.SensorCode))
        {
            devices.TryGetValue(s.DeviceId ?? Guid.Empty, out var d);
            ports.TryGetValue(s.ControllerPortId ?? s.BusPortId ?? Guid.Empty, out var p);
            var row = rows.GetValueOrDefault(s.FarmingRowId ?? d?.FarmingRowId ?? Guid.Empty);
            var area = s.FarmingAreaId ?? row?.FarmingAreaId ?? systems.GetValueOrDefault(s.WaterSystemId ?? Guid.Empty)?.FarmingAreaId ?? d?.FarmingAreaId;
            if (areaId.HasValue && area != areaId || rowId.HasValue && row?.Id != rowId) continue;
            var m = values.GetValueOrDefault(s.Id);
            result.Add(new { s.Id, s.SensorCode, s.SensorType, s.Name, s.Unit, s.IsActive, s.LastSeenAt,
                s.WaterSystemId, s.RasComponentId, s.DeviceId, s.ControllerPortId, s.BusPortId, s.BusAddress, s.BusRegister,
                controllerCode = d?.DeviceCode, controllerName = d?.Name, controllerStatus = d?.Status.ToString(),
                portCode = p?.PortCode, portType = p?.PortType, gpio = p?.Gpio, secondaryGpio = p?.SecondaryGpio,
                portPresent = p?.IsPresent, farmingAreaId = area, areaName = areas.GetValueOrDefault(area ?? Guid.Empty)?.Name,
                farmingRowId = row?.Id, rowName = row?.Name, s.InstallationLocation, s.SourceType,
                s.SamplingIntervalSeconds, disconnectTimeoutSeconds = s.DisconnectTimeoutSeconds ?? 900,
                s.ConfigurationVersion, s.AppliedConfigurationVersion, s.LastConfigurationAppliedAt,
                latestValue = m?.Value, latestMeasuredAt = m?.MeasuredAt, lastReceivedAt = m?.ReceivedAt,
                dataQuality = m?.DataQuality ?? "Unknown", status = Status(s,d,p), s.MinThreshold, s.MaxThreshold });
        }
        return result;
    }

    [HttpGet("inventory")]
    public async Task<IActionResult> InventoryList(Guid? deviceId, Guid? farmingAreaId, Guid? farmingRowId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<object>>.Ok(await Inventory(deviceId,farmingAreaId,farmingRowId,ct)));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var sensors = await db.Sensors.AsNoTracking().ToListAsync(ct);
        var devices = await db.Devices.AsNoTracking().ToDictionaryAsync(d=>d.Id,ct);
        var ports = await db.ControllerPorts.AsNoTracking().ToDictionaryAsync(p=>p.Id,ct);
        var states = sensors.Select(s => Status(s,devices.GetValueOrDefault(s.DeviceId ?? Guid.Empty),ports.GetValueOrDefault(s.ControllerPortId ?? s.BusPortId ?? Guid.Empty))).ToList();
        return Ok(ApiResponse<object>.Ok(new { total=sensors.Count, online=states.Count(s=>s=="Online"), offline=states.Count(s=>s=="Offline"),
            disabled=states.Count(s=>s=="Disabled"), unknown=states.Count(s=>s=="Unknown"), types=sensors.Select(s=>s.SensorType).Distinct().Count() }));
    }

    [HttpGet("types")]
    public async Task<IActionResult> Types(CancellationToken ct)
    {
        var stored = await db.Sensors.AsNoTracking().Select(s=>new {s.SensorType,s.Unit}).Distinct().ToListAsync(ct);
        var standard = new[] { ("Temperature","°C"),("pH","pH"),("DO","mg/L"),("Salinity","ppt"),("NO2","mg/L"),("NO3","mg/L"),("TDS","ppm"),("EC","µS/cm"),("Voltage","V"),("Current","A"),("State","state") };
        return Ok(ApiResponse<object>.Ok(standard.Select(x=>new {sensorType=x.Item1,unit=(string?)x.Item2})
            .Concat(stored.Select(x=>new {sensorType=x.SensorType,unit=x.Unit})).Distinct().ToList()));
    }

    [HttpGet("discovered")]
    public async Task<IActionResult> Discovered(Guid? deviceId, CancellationToken ct)
    {
        var ports = await (from p in db.ControllerPorts.AsNoTracking()
            join d in db.Devices.AsNoTracking() on p.DeviceId equals d.Id
            where p.IsPresent && p.ConfigVersion==d.CapabilityVersion && (deviceId==null||p.DeviceId==deviceId)
            && (p.PortType=="DIGITAL_INPUT"||p.PortType=="ANALOG_INPUT"||p.PortType=="I2C"||p.PortType=="RS485"||p.PortType=="UART")
            select new {p.Id,p.DeviceId,d.DeviceCode,p.PortCode,p.PortType,p.Gpio,p.SecondaryGpio,p.SensorCode,p.ConfigVersion,p.LastReportedAt,
                registered=db.Sensors.Any(s=>s.ControllerPortId==p.Id || s.BusPortId==p.Id)}).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(ports.Where(p=>!p.registered || p.PortType is "I2C" or "RS485" or "UART")));
    }

    private async Task<ControllerPort> RequirePort(Guid deviceId, Guid portId, CancellationToken ct)
    {
        var d=await db.Devices.FindAsync([deviceId],ct)??throw AppException.NotFound("Controller");
        var p=await db.ControllerPorts.FindAsync([portId],ct)??throw AppException.NotFound("Port");
        if(p.DeviceId!=deviceId||!p.IsPresent||p.ConfigVersion!=d.CapabilityVersion||p.PortType is not ("DIGITAL_INPUT" or "ANALOG_INPUT" or "I2C" or "RS485" or "UART"))
            throw AppException.Conflict("Port is not a current sensor input.");
        return p;
    }
    private static bool IsBus(ControllerPort p)=>p.PortType is "I2C" or "RS485" or "UART";
    private async Task Bind(Sensor s, ControllerPort p, string? address, string? register, CancellationToken ct)
    {
        if (p.SensorCode!=null && !IsBus(p) && p.SensorCode!=s.SensorCode) throw AppException.Conflict("Sensor code differs from manifest.");
        if (IsBus(p))
        {
            address=address?.Trim(); register=register?.Trim();
            if(string.IsNullOrWhiteSpace(address)||string.IsNullOrWhiteSpace(register)||address.Length>64||register.Length>64)
                throw AppException.BadRequest("Bus address and register/metric are required.");
            if(await db.Sensors.AnyAsync(x=>x.Id!=s.Id&&x.BusPortId==p.Id&&x.BusAddress==address&&x.BusRegister==register,ct))
                throw AppException.Conflict("Bus address/register already registered.");
            s.ControllerPortId=null;s.BusPortId=p.Id;s.BusAddress=address;s.BusRegister=register;
        }
        else
        {
            if(await db.Sensors.AnyAsync(x=>x.Id!=s.Id&&x.ControllerPortId==p.Id,ct))throw AppException.Conflict("Port already bound.");
            s.ControllerPortId=p.Id;s.BusPortId=null;s.BusAddress=null;s.BusRegister=null;
        }
        s.DeviceId=p.DeviceId;s.LastSeenAt=null;s.AppliedConfigurationVersion=null;s.LastConfigurationAppliedAt=null;
    }

    [HttpPost("register-from-port")]
    public async Task<IActionResult> Register(RegisterSensorFromPort req,CancellationToken ct)
    {
        var p=await RequirePort(req.ControllerId,req.PortId,ct);
        // Validate binding before persistence; explicit registration is required even for discovered hardware.
        var s=new Sensor {SensorCode=req.SensorCode?.Trim()??"",SensorType=req.SensorType?.Trim()??"",DeviceId=req.ControllerId,
            Name=req.Name?.Trim(),Unit=req.Unit?.Trim(),FarmingAreaId=req.FarmingAreaId,FarmingRowId=req.FarmingRowId,WaterSystemId=req.WaterSystemId,SourceType="Controller"};
        if(string.IsNullOrWhiteSpace(s.SensorCode)||s.SensorCode.Length>128||string.IsNullOrWhiteSpace(s.SensorType)||s.SensorType.Length>64||s.Unit?.Length>32||s.Name?.Length>200)
            throw AppException.BadRequest("Invalid sensor metadata.");
        if(await db.Sensors.AnyAsync(x=>x.SensorCode==s.SensorCode,ct))throw AppException.Conflict("Sensor code already exists.");
        await Bind(s,p,req.BusAddress,req.BusRegister,ct);
        await ValidateLocation(s,ct);
        db.Sensors.Add(s);Audit(s.Id,"RegisterSensorFromPort",p.PortCode);
        await Save(ct);
        return Ok(ApiResponse<object>.Ok(new {s.Id,s.SensorCode,s.ControllerPortId,s.BusPortId}));
    }

    private async Task ValidateLocation(Sensor s,CancellationToken ct)
    {
        var a=new HashSet<Guid>();
        if(s.FarmingAreaId is Guid area){if(!await db.FarmingAreas.AnyAsync(x=>x.Id==area,ct))throw AppException.NotFound("FarmingArea");a.Add(area);}
        if(s.FarmingRowId is Guid row){var r=await db.FarmingRows.FindAsync([row],ct)??throw AppException.NotFound("FarmingRow");a.Add(r.FarmingAreaId);}
        if(s.WaterSystemId is Guid ws){var w=await db.WaterSystems.FindAsync([ws],ct)??throw AppException.NotFound("WaterSystem");if(w.FarmingAreaId is Guid x)a.Add(x);}
        if(s.DeviceId is Guid did){var d=await db.Devices.FindAsync([did],ct)??throw AppException.NotFound("Controller");if(d.FarmingAreaId is Guid x)a.Add(x);
            if(d.FarmingRowId is Guid rid){var r=await db.FarmingRows.FindAsync([rid],ct)??throw AppException.NotFound("FarmingRow");a.Add(r.FarmingAreaId);}}
        if(a.Count>1)throw AppException.BadRequest("Sensor and controller must belong to the same farming area.");
    }
    private async Task Save(CancellationToken ct){try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){throw AppException.Conflict("Sensor configuration changed or conflicts with an existing binding.");}}

    [HttpPut("{id:guid}/binding")]
    public async Task<IActionResult> Binding(Guid id,SensorBindingRequest req,CancellationToken ct)
    {
        var s=await Sensor(id,ct);
        if(req.PortId.HasValue){if(!req.DeviceId.HasValue)throw AppException.BadRequest("DeviceId required.");await Bind(s,await RequirePort(req.DeviceId.Value,req.PortId.Value,ct),req.BusAddress,req.BusRegister,ct);}
        else {s.DeviceId=req.DeviceId;s.ControllerPortId=null;s.BusPortId=null;s.BusAddress=null;s.BusRegister=null;s.LastSeenAt=null;s.AppliedConfigurationVersion=null;s.LastConfigurationAppliedAt=null;}
        await ValidateLocation(s,ct);Audit(id,"UpdateSensorBinding");await Save(ct);
        return Ok(ApiResponse<object>.Ok(new{s.Id,s.DeviceId,s.ControllerPortId,s.BusPortId,s.BusAddress,s.BusRegister}));
    }

    [HttpPost("{id:guid}/enable")]
    public Task<IActionResult> Enable(Guid id,CancellationToken ct)=>SetActive(id,true,ct);
    [HttpPost("{id:guid}/disable")]
    public Task<IActionResult> Disable(Guid id,CancellationToken ct)=>SetActive(id,false,ct);
    private async Task<IActionResult> SetActive(Guid id,bool enabled,CancellationToken ct)
    {
        var s=await Sensor(id,ct);s.IsActive=enabled;s.LastSeenAt=null;
        if(!enabled)
        {
            var alerts=await db.Alerts.Where(a=>a.SensorId==id&&a.IncidentType=="sensor_disconnect"&&(a.Status==AlertStatus.Active||a.Status==AlertStatus.Acknowledged||a.Status==AlertStatus.InProgress)).ToListAsync(ct);
            foreach(var a in alerts){a.Status=AlertStatus.Recovered;a.ResolvedAt=DateTime.UtcNow;a.ResolutionReason="Sensor disabled by administrator.";a.ResolvedBy=Actor();}
        }
        Audit(id,enabled?"EnableSensor":"DisableSensor");await Save(ct);
        return Ok(ApiResponse<object>.Ok(new{s.Id,s.IsActive,status=enabled?"Unknown":"Disabled"}));
    }

    [HttpGet("{id:guid}/health")]
    public async Task<IActionResult> Health(Guid id,CancellationToken ct)
    {
        var s=await Sensor(id,ct);
        var d=s.DeviceId is Guid did?await db.Devices.FindAsync([did],ct):null;
        var portId=s.ControllerPortId??s.BusPortId;
        var p=portId is Guid pid?await db.ControllerPorts.FindAsync([pid],ct):null;
        var latest=await db.WaterMeasurements.AsNoTracking().Where(m=>m.SensorId==id).OrderByDescending(m=>m.MeasuredAt).FirstOrDefaultAsync(ct);
        return Ok(ApiResponse<object>.Ok(new {s.Id,status=Status(s,d,p),s.IsActive,s.LastSeenAt,controllerStatus=d?.Status.ToString(),
            portPresent=p?.IsPresent,portCurrent=p!=null&&d!=null&&p.ConfigVersion==d.CapabilityVersion,
            latestValue=latest?.Value,latestMeasuredAt=latest?.MeasuredAt,lastReceivedAt=latest?.ReceivedAt,dataQuality=latest?.DataQuality??"Unknown",
            timeoutSeconds=s.DisconnectTimeoutSeconds??900,verification="TelemetryOnly"}));
    }
    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> Test(Guid id,CancellationToken ct)
    {
        _=await Sensor(id,ct);Audit(id,"SensorTestRequested","No remote sample protocol integrated");await Save(ct);
        return StatusCode(501,ApiResponse<object>.Fail("Remote sample test not integrated. Use health to inspect received telemetry."));
    }

    private IQueryable<WaterMeasurement> Measurements(Guid id,DateTimeOffset? from,DateTimeOffset? to)
    {
        if(from.HasValue&&to.HasValue&&from>=to)throw AppException.BadRequest("From must precede To.");
        return db.WaterMeasurements.AsNoTracking().Where(m=>m.SensorId==id&&(!from.HasValue||m.MeasuredAt>=from.Value.UtcDateTime)&&(!to.HasValue||m.MeasuredAt<to.Value.UtcDateTime));
    }
    [HttpGet("{id:guid}/measurements")]
    public async Task<IActionResult> History(Guid id,DateTimeOffset? from,DateTimeOffset? to,int page=1,int pageSize=100,CancellationToken ct=default)
    {
        _=await Sensor(id,ct);
        if(page<1||pageSize is <1 or >2000||(long)(page-1)*pageSize>int.MaxValue)throw AppException.BadRequest("Invalid pagination.");
        var q=Measurements(id,from,to);var total=await q.CountAsync(ct);
        var items=await q.OrderByDescending(m=>m.MeasuredAt).ThenByDescending(m=>m.Id).Skip((page-1)*pageSize).Take(pageSize)
            .Select(m=>new {m.Id,m.SensorId,m.Value,m.Unit,m.MeasuredAt,m.ReceivedAt,m.Source,m.DataQuality}).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{items,totalCount=total,page,pageSize,totalPages=(int)Math.Ceiling((double)total/pageSize)}));
    }
    [HttpGet("{id:guid}/statistics")]
    public async Task<IActionResult> Statistics(Guid id,DateTimeOffset? from,DateTimeOffset? to,CancellationToken ct)
    {
        _=await Sensor(id,ct);
        var groups=await Measurements(id,from,to).GroupBy(m=>m.Unit).Select(g=>new{unit=g.Key,count=g.Count(),min=g.Min(m=>m.Value),max=g.Max(m=>m.Value),average=g.Average(m=>m.Value)}).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{sensorId=id,groups}));
    }
    [HttpGet("{id:guid}/chart")]
    public async Task<IActionResult> Chart(Guid id,DateTimeOffset? from,DateTimeOffset? to,int bucketMinutes=60,CancellationToken ct=default)
    {
        _=await Sensor(id,ct);var end=to??DateTimeOffset.UtcNow;var start=from??end.AddHours(-24);
        if(bucketMinutes<1||bucketMinutes>1440||end<=start||end-start>TimeSpan.FromDays(31)||(end-start).TotalMinutes/bucketMinutes>2000)
            throw AppException.BadRequest("Chart range max 31 days and 2000 buckets.");
        // Bound raw rows; clients may narrow range for higher-frequency sensors.
        var values=await Measurements(id,start,end).OrderBy(m=>m.MeasuredAt).Take(100001).Select(m=>new{m.MeasuredAt,m.Value,m.Unit}).ToListAsync(ct);
        if(values.Count>100000)throw AppException.BadRequest("Too many readings; narrow chart range.");
        var ticks=TimeSpan.FromMinutes(bucketMinutes).Ticks;
        var buckets=values.GroupBy(m=>new {at=new DateTime(m.MeasuredAt.Ticks/ticks*ticks,DateTimeKind.Utc),m.Unit})
            .Select(g=>new{g.Key.at,g.Key.Unit,count=g.Count(),min=g.Min(m=>m.Value),max=g.Max(m=>m.Value),average=g.Average(m=>m.Value)}).OrderBy(x=>x.at);
        return Ok(ApiResponse<object>.Ok(new{sensorId=id,bucketMinutes,buckets}));
    }

    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id,CancellationToken ct)
    {
        _=await Sensor(id,ct);
        var alerts=await db.Alerts.AsNoTracking().Where(a=>a.SensorId==id&&a.SourceType=="Sensor"&&(a.IncidentType=="sensor_disconnect"||a.IncidentType=="sensor_read_error"))
            .OrderByDescending(a=>a.LastOccurredAt).Take(200).Select(a=>new{a.Id,a.IncidentType,a.Message,status=a.Status.ToString(),severity=a.Severity.ToString(),a.LastOccurredAt,a.ResolvedAt}).ToListAsync(ct);
        var logs=await db.OperationLogs.AsNoTracking().Where(l=>l.EntityId==id&&l.EntityType=="Sensor").OrderByDescending(l=>l.CreatedAt).Take(200)
            .Select(l=>new{l.Id,l.Action,l.CreatedAt,l.UserId}).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{alerts,logs}));
    }
    private static object Config(Sensor s)=>new{s.Id,s.SamplingIntervalSeconds,disconnectTimeoutSeconds=s.DisconnectTimeoutSeconds??900,
        s.ConfigurationVersion,s.AppliedConfigurationVersion,s.LastConfigurationAppliedAt,
        runtimeStatus=s.ConfigurationVersion==0?"NotConfigured":s.ConfigurationVersion==s.AppliedConfigurationVersion?"Applied":"Pending"};
    [HttpGet("{id:guid}/configuration")]
    public async Task<IActionResult> Configuration(Guid id,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(Config(await Sensor(id,ct))));
    [HttpPut("{id:guid}/configuration")]
    public async Task<IActionResult> Configuration(Guid id,SensorConfigurationRequest req,CancellationToken ct)
    {
        var s=await Sensor(id,ct);
        if(req.SamplingIntervalSeconds is <1 or >86400||req.DisconnectTimeoutSeconds<req.SamplingIntervalSeconds||req.DisconnectTimeoutSeconds>604800)
            throw AppException.BadRequest("Invalid sampling interval or timeout.");
        if(req.ExpectedVersion!=s.ConfigurationVersion)throw AppException.Conflict("Configuration version changed.");
        s.SamplingIntervalSeconds=req.SamplingIntervalSeconds;s.DisconnectTimeoutSeconds=req.DisconnectTimeoutSeconds;s.ConfigurationVersion++;
        Audit(id,"UpdateSensorConfiguration");await Save(ct);return Ok(ApiResponse<object>.Ok(Config(s)));
    }

    [HttpGet("{id:guid}/calibrations")]
    public async Task<IActionResult> Calibrations(Guid id,CancellationToken ct)
    {
        _=await Sensor(id,ct);return Ok(ApiResponse<object>.Ok(await db.SensorCalibrations.AsNoTracking().Where(c=>c.SensorId==id).OrderByDescending(c=>c.PerformedAt).Take(200).ToListAsync(ct)));
    }
    [HttpPost("{id:guid}/calibrations")]
    public async Task<IActionResult> Calibration(Guid id,SensorCalibrationRequest req,CancellationToken ct)
    {
        var s=await Sensor(id,ct);
        if(req.PerformedAt==default||req.PerformedAt>DateTimeOffset.UtcNow.AddMinutes(2)||string.IsNullOrWhiteSpace(req.Unit)||req.Unit.Length>32
            ||s.Unit!=null&&!string.Equals(s.Unit,req.Unit,StringComparison.OrdinalIgnoreCase)
            ||string.IsNullOrWhiteSpace(req.Method)||req.Method.Length>100||req.Result is not ("Passed" or "Failed")||req.Note?.Length>500)
            throw AppException.BadRequest("Invalid manual calibration record.");
        var c=new SensorCalibration{SensorId=id,ActorId=Actor(),PerformedAt=req.PerformedAt.UtcDateTime,ReferenceValue=req.ReferenceValue,
            ObservedValue=req.ObservedValue,Unit=req.Unit,Method=req.Method,Result=req.Result,Note=req.Note};
        db.SensorCalibrations.Add(c);Audit(id,"RecordSensorCalibration",req.Result);await Save(ct);
        return Ok(ApiResponse<object>.Ok(c,"Manual calibration recorded; no hardware adjustment was performed."));
    }
}
