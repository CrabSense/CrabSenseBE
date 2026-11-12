using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace CrabSenseBE.Application.Services;

public class RasFlowService : IRasFlowService
{
    private readonly IUnitOfWork _uow;
    private readonly IEdgeCommandService _edgeCommands;
    private readonly FarmAreaScope _areas;
    private readonly IConfiguration? _configuration;

    public RasFlowService(
        IUnitOfWork uow,
        IEdgeCommandService edgeCommands,
        ICurrentUserService? currentUser = null,
        IConfiguration? configuration = null)
    {
        _uow = uow;
        _edgeCommands = edgeCommands;
        _areas = new FarmAreaScope(uow, currentUser);
        _configuration = configuration;
    }

    public async Task<ApiResponse<RasFlowDiagramDto>> GetDiagramByAreaAsync(Guid areaId, CancellationToken ct = default)
    {
        await _areas.EnsureReadAsync(areaId, ct);
        var area = await RequireAreaAsync(areaId, ct);
        var ws = await FindSystemAsync(area, ct);
        if (ws is null)
            return ApiResponse<RasFlowDiagramDto>.Ok(EmptyDiagram(area));
        return ApiResponse<RasFlowDiagramDto>.Ok(await BuildDiagramAsync(area, ws, ct));
    }

    public async Task<ApiResponse<RasFlowDiagramDto>> AddNodeAsync(
        Guid areaId, CreateRasFlowNodeRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.NodeCode))
            throw AppException.BadRequest("nodeCode is required.");
        if (string.IsNullOrWhiteSpace(req.DisplayLabel))
            throw AppException.BadRequest("displayLabel is required.");

        await _areas.EnsureAsync(areaId, manage: true, ct);
        var area = await RequireAreaAsync(areaId, ct);
        var ws = await EnsureSystemAsync(area, ct);
        await EnsureDefaultPipelineAsync(ws, ct);

        var code = req.NodeCode.Trim();
        var existing = (await _uow.RasComponents.FindAsync(c => c.WaterSystemId == ws.Id, ct)).ToList();
        if (existing.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
            throw AppException.Conflict($"NodeCode '{code}' already exists on this RAS.");

        var type = string.IsNullOrWhiteSpace(req.Type)
            ? RasComponentCatalog.TypeFromCode(code)
            : req.Type.Trim().ToUpperInvariant();
        var position = req.SortOrder;
        if (position <= 0 && existing.Count > 0)
            position = existing.Max(c => c.Position) + 1;

        var node = new RasComponent
        {
            WaterSystemId = ws.Id,
            Code = code,
            Name = req.DisplayLabel.Trim(),
            Type = type,
            Status = "active",
            Position = position,
            Capacity = req.Capacity,
            Description = req.Description,
            NodeType = string.IsNullOrWhiteSpace(req.NodeType) ? "equipment" : req.NodeType.Trim(),
            IconKey = RasComponentCatalog.DefaultIcon(type, code),
            RelayDeviceId = req.RelayDeviceId,
            RelayChannel = string.IsNullOrWhiteSpace(req.RelayChannel) ? null : req.RelayChannel.Trim(),
            ParamDefaultsJson = SerializeParams(req.ParamDefaults, req.ParamDefaultsJson),
            HasRelay = req.RelayDeviceId.HasValue || !string.IsNullOrWhiteSpace(req.RelayChannel),
            IsOn = false
        };
        await _uow.RasComponents.AddAsync(node, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<RasFlowDiagramDto>.Ok(await BuildDiagramAsync(area, ws, ct), "Node added.");
    }

    public async Task<ApiResponse<RasFlowDiagramDto>> UpdateNodeRelayAsync(
        Guid areaId, Guid nodeId, UpdateRasFlowNodeRelayRequest req, CancellationToken ct = default)
    {
        var actor = await _areas.CurrentUserAsync(ct);
        if (actor is not null && actor.Role != UserRole.SystemAdministrator)
            throw AppException.Forbidden("Cấu hình relay phần cứng thuộc System Administrator.");

        var area = await RequireAreaAsync(areaId, ct);
        var ws = await FindSystemAsync(area, ct) ?? throw AppException.NotFound("WaterSystem");
        var node = await _uow.RasComponents.GetByIdAsync(nodeId, ct)
            ?? throw AppException.NotFound("RasComponent");
        if (node.WaterSystemId != ws.Id)
            throw AppException.BadRequest("Node does not belong to this area RAS.");

        var relayDeviceId = req.RelayDeviceId;
        if (relayDeviceId is null && !string.IsNullOrWhiteSpace(req.RelayDeviceCode))
        {
            var device = await _uow.Devices.FirstOrDefaultAsync(
                d => d.DeviceCode == req.RelayDeviceCode.Trim(), ct);
            relayDeviceId = device?.Id;
        }

        if (relayDeviceId is null || string.IsNullOrWhiteSpace(req.RelayChannel))
        {
            node.RelayDeviceId = null;
            node.RelayChannel = null;
            node.HasRelay = false;
            node.IsOn = false;
            node.LastReportedAt = null;
            node.RunStartedAt = null;
            node.ControlMode = null;
        }
        else
        {
            var device = await _uow.Devices.GetByIdAsync(relayDeviceId.Value, ct)
                ?? throw AppException.NotFound("Controller");
            EnsureDeviceArea(device, area.Id);
            if (!int.TryParse(req.RelayChannel.Trim(), out var channelNo) || channelNo < 1)
                throw AppException.BadRequest("SSR channel must be a positive number.");
            node.RelayDeviceId = device.Id;
            node.RelayChannel = req.RelayChannel.Trim();
            node.HasRelay = true;
            node.IsOn = false;
            node.LastReportedAt = null;
            node.RunStartedAt = null;
            node.ControlMode = "manual";
        }
        if (!string.IsNullOrWhiteSpace(req.ParamDefaultsJson))
            node.ParamDefaultsJson = req.ParamDefaultsJson.Trim();

        _uow.RasComponents.Update(node);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<RasFlowDiagramDto>.Ok(
            await BuildDiagramAsync(area, ws, ct), "Relay assignment updated.");
    }

    public async Task<ApiResponse<RasFlowDiagramDto>> ReorderAsync(
        Guid areaId, ReorderRasFlowRequest req, CancellationToken ct = default)
    {
        if (req.NodeIds is null || req.NodeIds.Count == 0)
            throw AppException.BadRequest("nodeIds is required.");

        await _areas.EnsureAsync(areaId, manage: true, ct);
        var area = await RequireAreaAsync(areaId, ct);
        var ws = await EnsureSystemAsync(area, ct);
        var comps = (await _uow.RasComponents.FindAsync(c => c.WaterSystemId == ws.Id, ct)).ToList();
        var byId = comps.ToDictionary(c => c.Id);
        for (var i = 0; i < req.NodeIds.Count; i++)
        {
            if (!byId.TryGetValue(req.NodeIds[i], out var node))
                throw AppException.NotFound("RasComponent");
            node.Position = i;
            _uow.RasComponents.Update(node);
        }

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<RasFlowDiagramDto>.Ok(await BuildDiagramAsync(area, ws, ct), "Reordered.");
    }

    public async Task<ApiResponse<RasFlowDiagramDto>> AddFlowAsync(
        Guid areaId, CreateWaterFlowRequest req, CancellationToken ct = default)
    {
        if (req.FromComponentId == Guid.Empty || req.ToComponentId == Guid.Empty)
            throw AppException.BadRequest("fromComponentId and toComponentId are required.");
        if (req.FromComponentId == req.ToComponentId)
            throw AppException.BadRequest("A node cannot connect to itself.");

        await _areas.EnsureAsync(areaId, manage: true, ct);
        var area = await RequireAreaAsync(areaId, ct);
        var ws = await EnsureSystemAsync(area, ct);
        var from = await _uow.RasComponents.GetByIdAsync(req.FromComponentId, ct)
            ?? throw AppException.NotFound("RasComponent");
        var to = await _uow.RasComponents.GetByIdAsync(req.ToComponentId, ct)
            ?? throw AppException.NotFound("RasComponent");
        if (from.WaterSystemId != ws.Id || to.WaterSystemId != ws.Id)
            throw AppException.BadRequest("Both nodes must belong to this area RAS.");

        var exists = await _uow.WaterFlows.AnyAsync(
            f => f.WaterSystemId == ws.Id
                 && f.FromComponentId == from.Id
                 && f.ToComponentId == to.Id, ct);
        if (!exists)
        {
            await _uow.WaterFlows.AddAsync(new WaterFlow
            {
                WaterSystemId = ws.Id,
                FromComponentId = from.Id,
                ToComponentId = to.Id,
                Status = "active",
                SortOrder = 0
            }, ct);
            await _uow.SaveChangesAsync(ct);
        }

        return ApiResponse<RasFlowDiagramDto>.Ok(await BuildDiagramAsync(area, ws, ct), "Flow added.");
    }

    public async Task<ApiResponse<RasFlowDiagramDto>> DeleteFlowAsync(
        Guid areaId, Guid flowId, CancellationToken ct = default)
    {
        await _areas.EnsureAsync(areaId, manage: true, ct);
        var area = await RequireAreaAsync(areaId, ct);
        var ws = await EnsureSystemAsync(area, ct);
        var flow = await _uow.WaterFlows.GetByIdAsync(flowId, ct)
            ?? throw AppException.NotFound("WaterFlow");
        if (flow.WaterSystemId != ws.Id)
            throw AppException.BadRequest("Flow does not belong to this area RAS.");
        _uow.WaterFlows.Remove(flow);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<RasFlowDiagramDto>.Ok(await BuildDiagramAsync(area, ws, ct), "Flow removed.");
    }

    public async Task<ApiResponse> DeleteNodeAsync(Guid areaId, Guid nodeId, CancellationToken ct = default)
    {
        await _areas.EnsureAsync(areaId, manage: true, ct);
        var area = await RequireAreaAsync(areaId, ct);
        var ws = await EnsureSystemAsync(area, ct);
        var node = await _uow.RasComponents.GetByIdAsync(nodeId, ct)
            ?? throw AppException.NotFound("RasComponent");
        if (node.WaterSystemId != ws.Id)
            throw AppException.BadRequest("Node does not belong to this area RAS.");

        var flows = (await _uow.WaterFlows.FindAsync(
            f => f.FromComponentId == nodeId || f.ToComponentId == nodeId, ct)).ToList();
        foreach (var f in flows) _uow.WaterFlows.Remove(f);

        var sensors = (await _uow.Sensors.FindAsync(s => s.RasComponentId == nodeId, ct)).ToList();
        foreach (var s in sensors)
        {
            s.RasComponentId = null;
            _uow.Sensors.Update(s);
        }

        _uow.RasComponents.Remove(node);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Node deleted.");
    }

    public async Task<ApiResponse<RasFlowDiagramDto>> CommandAsync(
        Guid areaId, Guid nodeId, RasFlowCommandRequest req, Guid? actorId = null, CancellationToken ct = default)
    {
        var actor = await _areas.CurrentUserAsync(ct);
        if (actor is not null && actor.Role is not (UserRole.FarmManager or UserRole.FarmStaff))
            throw AppException.Forbidden("Chỉ Farm Manager và Farm Staff được điều khiển vận hành RAS.");
        if (actorId is not Guid commandActorId
            || commandActorId == Guid.Empty
            || actor is not null && actor.Id != commandActorId)
            throw AppException.Unauthorized(
                "Không xác định được người gửi lệnh RAS thủ công.");
        await _areas.EnsureAsync(areaId, manage: false, ct);

        var area = await RequireAreaAsync(areaId, ct);
        var ws = await FindSystemAsync(area, ct) ?? throw AppException.NotFound("WaterSystem");
        var node = await _uow.RasComponents.GetByIdAsync(nodeId, ct)
            ?? throw AppException.NotFound("RasComponent");
        if (node.WaterSystemId != ws.Id)
            throw AppException.BadRequest("Node does not belong to this area RAS.");
        if (node.RelayDeviceId is Guid linkedDeviceId)
        {
            var linked = await _uow.Devices.GetByIdAsync(linkedDeviceId, ct)
                ?? throw AppException.NotFound("Controller");
            EnsureDeviceArea(linked, area.Id);
        }
        if (!node.HasRelay)
            throw AppException.BadRequest("Node has no relay.");

        var cmd = (req.Command ?? "").Trim().ToLowerInvariant();
        if (cmd == "toggle" && !node.LastReportedAt.HasValue)
            throw AppException.Conflict("Chưa biết trạng thái relay; hãy chọn bật hoặc tắt rõ ràng.");
        bool? desired = cmd switch
        {
            "on" or "start" or "open" => true,
            "off" or "stop" or "close" => false,
            "toggle" => !node.IsOn,
            _ => null
        };
        if (desired is null && cmd is not ("auto" or "manual"))
            throw AppException.BadRequest("Command must be auto, manual, on, off, start, stop, or toggle.");

        if (desired.HasValue && (node.RelayDeviceId is null || string.IsNullOrWhiteSpace(node.RelayChannel)))
            throw AppException.Conflict("Node chưa được gán controller và channel.");

        // Tracked by EF; write only the control-mode property, never a stale observation.
        node.ControlMode = cmd == "auto" ? "auto" : "manual";
        var title = desired.HasValue
            ? $"Yêu cầu {(desired.Value ? "bật" : "tắt")} {node.Name}"
            : $"Chuyển {node.Name} sang {node.ControlMode}";
        await _uow.OperationLogs.AddAsync(new OperationLog
        {
            UserId = commandActorId,
            Action = desired.HasValue ? $"ras_request_{(desired.Value ? "on" : "off")}" : cmd,
            EntityType = "RasComponent",
            EntityId = node.Id,
            Details = $"{area.Id:N}|{title}"
        }, ct);

        if (desired.HasValue)
        {
            // The edge service uses the same scoped unit of work and saves the
            // request, mode and audit together. Do not save before enqueue.
            await EnqueueRelayAsync(node, desired.Value, actorId, ct);
        }
        else
        {
            await _uow.SaveChangesAsync(ct);
        }
        return ApiResponse<RasFlowDiagramDto>.Ok(
            await BuildDiagramAsync(area, ws, ct),
            desired.HasValue ? "Command queued; waiting for device relay-state." : "Control mode updated.");
    }

    public async Task<ApiResponse<IReadOnlyList<RasComponentDto>>> ListComponentsAsync(
        Guid waterSystemId, CancellationToken ct = default)
    {
        var system = await _uow.WaterSystems.GetByIdAsync(waterSystemId, ct) ?? throw AppException.NotFound("WaterSystem");
        if (system.FarmingAreaId is Guid componentAreaId)
            await _areas.EnsureReadAsync(componentAreaId, ct);
        var list = (await _uow.RasComponents.FindAsync(c => c.WaterSystemId == waterSystemId, ct))
            .OrderBy(c => c.Position)
            .Select(MapComponent)
            .ToList();
        return ApiResponse<IReadOnlyList<RasComponentDto>>.Ok(list);
    }

    public async Task<ApiResponse<IReadOnlyList<WaterFlowDto>>> ListFlowsAsync(
        Guid waterSystemId, CancellationToken ct = default)
    {
        var system = await _uow.WaterSystems.GetByIdAsync(waterSystemId, ct) ?? throw AppException.NotFound("WaterSystem");
        if (system.FarmingAreaId is Guid flowAreaId)
            await _areas.EnsureReadAsync(flowAreaId, ct);
        var list = (await _uow.WaterFlows.FindAsync(f => f.WaterSystemId == waterSystemId, ct))
            .OrderBy(f => f.SortOrder)
            .Select(MapFlow)
            .ToList();
        return ApiResponse<IReadOnlyList<WaterFlowDto>>.Ok(list);
    }

    private async Task<FarmingArea> RequireAreaAsync(Guid areaId, CancellationToken ct)
        => await _uow.FarmingAreas.GetByIdAsync(areaId, ct) ?? throw AppException.NotFound("FarmingArea");

    private async Task<WaterSystem?> FindSystemAsync(FarmingArea area, CancellationToken ct)
    {
        var list = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == area.Id, ct)).ToList();
        return list.FirstOrDefault(w => string.Equals(w.Type, "RAS", StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault();
    }

    private static RasFlowDiagramDto EmptyDiagram(FarmingArea area) => new(
        area.Id,
        area.Code,
        area.Name,
        Guid.Empty,
        "",
        null,
        Array.Empty<RasFlowNodeDto>(),
        Array.Empty<WaterFlowDto>(),
        0,
        0,
        0,
        0);

    private static void EnsureDeviceArea(Device device, Guid areaId)
    {
        if (device.FarmingAreaId != areaId)
            throw AppException.Forbidden("Thiết bị relay không thuộc khu này.");
    }

    private async Task<WaterSystem> EnsureSystemAsync(FarmingArea area, CancellationToken ct)
    {
        var existing = await FindSystemAsync(area, ct);
        if (existing is not null) return existing;

        var ws = new WaterSystem
        {
            FarmingAreaId = area.Id,
            Name = string.IsNullOrWhiteSpace(area.Name) ? "RAS" : $"RAS {area.Name}",
            Type = "RAS",
            IsActive = true,
            Status = "active",
            FlowStatus = "ok"
        };
        await _uow.WaterSystems.AddAsync(ws, ct);
        await _uow.SaveChangesAsync(ct);
        return ws;
    }

    private async Task EnsureDefaultPipelineAsync(WaterSystem ws, CancellationToken ct)
    {
        if (await _uow.RasComponents.AnyAsync(c => c.WaterSystemId == ws.Id, ct))
            return;

        var created = new List<RasComponent>();
        var i = 0;
        foreach (var (code, name, type) in RasComponentCatalog.DefaultPipeline)
        {
            var node = new RasComponent
            {
                WaterSystemId = ws.Id,
                Code = code,
                Name = name,
                Type = type,
                Status = "active",
                Position = i,
                NodeType = code == "crab_boxes" ? "source" : "equipment",
                IconKey = code,
                HasRelay = code is "drum" or "skimmer" or "bio" or "pump",
                IsOn = code is "drum" or "skimmer" or "bio"
            };
            await _uow.RasComponents.AddAsync(node, ct);
            created.Add(node);
            i++;
        }
        await _uow.SaveChangesAsync(ct);

        for (var f = 0; f < created.Count - 1; f++)
        {
            await _uow.WaterFlows.AddAsync(new WaterFlow
            {
                WaterSystemId = ws.Id,
                FromComponentId = created[f].Id,
                ToComponentId = created[f + 1].Id,
                Status = "active",
                SortOrder = f
            }, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<RasFlowDiagramDto> BuildDiagramAsync(FarmingArea area, WaterSystem ws, CancellationToken ct)
    {
        var comps = (await _uow.RasComponents.FindAsync(c => c.WaterSystemId == ws.Id, ct))
            .OrderBy(c => c.Position).ThenBy(c => c.CreatedAt).ToList();
        var flows = (await _uow.WaterFlows.FindAsync(f => f.WaterSystemId == ws.Id, ct))
            .OrderBy(f => f.SortOrder).ToList();
        var sensors = (await _uow.Sensors.FindAsync(
            s => s.WaterSystemId == ws.Id || (s.RasComponentId != null && comps.Select(c => c.Id).Contains(s.RasComponentId.Value)),
            ct)).ToList();
        var sensorIds = sensors.Select(s => s.Id).ToHashSet();
        var latest = sensorIds.Count == 0
            ? new Dictionary<Guid, WaterMeasurement>()
            : (await _uow.WaterMeasurements.GetAllAsync(ct))
                .Where(m => sensorIds.Contains(m.SensorId))
                .GroupBy(m => m.SensorId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.MeasuredAt).First());

        var nodes = new List<RasFlowNodeDto>(comps.Count);
        foreach (var component in comps)
        {
            var mapped = MapNode(component, sensors, latest, flows);
            if (component.HasRelay && component.RelayDeviceId is Guid relayId)
            {
                var device = await _uow.Devices.GetByIdAsync(relayId, ct);
                var command = device is null ? null
                    : await LatestRelayCommandAsync(device.DeviceCode, component.RelayChannel, ct);
                var online = device?.Status == DeviceStatus.Online
                    && device.EdgeState is "Approved" or "Online";
                mapped = mapped with
                {
                    IsOnline = online,
                    ConnectionLabel = online ? "Online" : "Offline",
                    IsOn = online && component.LastReportedAt.HasValue ? mapped.IsOn : null,
                    LastReportedAt = component.LastReportedAt,
                    DesiredIsOn = command?.Command switch { "on" => true, "off" or "alloff" => false, _ => null },
                    CommandStatus = command is null ? null : RelayCommandStatus(command, component),
                    CommandId = command?.Id
                };
            }
            nodes.Add(mapped);
        }
        var ids = comps.Select(c => c.Id).ToHashSet();
        var logs = (await _uow.OperationLogs.FindAsync(
                l => l.EntityType == "RasComponent" && l.EntityId != null && ids.Contains(l.EntityId.Value), ct))
            .OrderByDescending(l => l.CreatedAt)
            .Take(20)
            .Select(l => new RasControlEventDto(
                l.CreatedAt,
                l.Action,
                l.Details is { } d && d.Contains('|') ? d[(d.IndexOf('|') + 1)..] : $"{l.Action}",
                l.Details))
            .ToList();
        return new RasFlowDiagramDto(
            area.Id,
            area.Code,
            area.Name,
            ws.Id,
            ws.Name,
            ws.FlowStatus,
            nodes,
            flows.Select(MapFlow).ToList(),
            nodes.Sum(n => n.PowerW ?? 0),
            nodes.Count(n => n.IsOn == true),
            nodes.Count(n => n.HasRelay),
            nodes.Count(n => n.IsOnline != false),
            nodes.Count(n => n.HasRelay && n.IsOn == false),
            nodes.Count(n => n.Status is "alarm" or "error" or "unknown" or "unsafe"
                || n.IsOnline == false),
            logs);
    }

    private static RasFlowNodeDto MapNode(
        RasComponent c,
        IReadOnlyList<Sensor> sensors,
        IReadOnlyDictionary<Guid, WaterMeasurement> latest,
        IReadOnlyList<WaterFlow> flows)
    {
        var attached = sensors.Where(s => s.RasComponentId == c.Id).ToList();
        decimal? temp = FirstValue(attached, latest, "Temperature", "temp");
        decimal? flowLph = flows.FirstOrDefault(f => f.FromComponentId == c.Id || f.ToComponentId == c.Id)?.FlowRate
            ?? FirstValue(attached, latest, "Flow", "flow");
        decimal? level = FirstValue(attached, latest, "Level", "level");
        var metric = BuildMetric(c, temp, flowLph, attached, latest);
        var online = c.Status is not "stopped" and not "offline";
        return new RasFlowNodeDto(
            c.Id,
            c.Code,
            c.Name,
            c.Position,
            string.IsNullOrWhiteSpace(c.NodeType) ? "equipment" : c.NodeType,
            c.RelayDeviceId?.ToString(),
            c.RelayChannel,
            c.ParamDefaultsJson,
            string.IsNullOrWhiteSpace(c.IconKey) ? c.Code : c.IconKey,
            metric.label,
            metric.secondary,
            c.HasRelay,
            c.HasRelay && c.LastReportedAt.HasValue ? c.IsOn : null,
            online,
            online ? "Online" : "Offline",
            c.ControlMode,
            c.Status == "alarm" ? c.Description : null,
            PowerW: null,
            CurrentA: null,
            VoltageV: null,
            FlowLpm: flowLph.HasValue ? flowLph / 60m : null,
            FlowLph: flowLph,
            TempC: temp,
            LevelPercent: level,
            Type: c.Type,
            Status: c.Status,
            Capacity: c.Capacity,
            LastCommandAt: c.LastCommandAt,
            RunStartedAt: c.RunStartedAt);
    }

    private static (string label, string? secondary) BuildMetric(
        RasComponent c,
        decimal? temp,
        decimal? flowLph,
        IReadOnlyList<Sensor> attached,
        IReadOnlyDictionary<Guid, WaterMeasurement> latest)
    {
        if (flowLph is decimal f) return ($"{f:0} L/h", temp is decimal t ? $"{t:0.0} °C" : null);
        var ph = FirstValue(attached, latest, "pH", "ph");
        if (ph is decimal p) return ($"pH {p:0.00}", temp is decimal t2 ? $"{t2:0.0} °C" : null);
        if (temp is decimal t3) return ($"{t3:0.0} °C", null);
        if (c.Capacity is decimal cap) return ($"{cap:0} L", null);
        return ("—", null);
    }

    private static decimal? FirstValue(
        IReadOnlyList<Sensor> sensors,
        IReadOnlyDictionary<Guid, WaterMeasurement> latest,
        params string[] types)
    {
        foreach (var s in sensors)
        {
            if (!types.Any(t => s.SensorType.Contains(t, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (latest.TryGetValue(s.Id, out var m)) return m.Value;
        }
        return null;
    }

    private static string? SerializeParams(object? obj, string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw)) return raw.Trim();
        if (obj is null) return null;
        if (obj is string s) return s;
        return JsonSerializer.Serialize(obj);
    }

    public async Task ApplyAutoRelaysAsync(Guid deviceId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var device = await _uow.Devices.GetByIdAsync(deviceId, ct);
        var pumps = (await _uow.RasComponents.FindAsync(
            c => c.RelayDeviceId == deviceId
                 && c.HasRelay
                 && c.ControlMode != null
                 && c.ControlMode.ToLower() == "auto",
            ct)).ToList();
        if (pumps.Count == 0)
            return;

        var controllerReady = device is not null
            && device.Status == DeviceStatus.Online
            && device.EdgeState is "Approved" or "Online";
        var firmwareInterlockVerified = _configuration?.GetValue(
            "RasAutomation:FirmwareInterlockVerified", false) ?? false;
        var futureTolerance = TimeSpan.FromSeconds(Math.Clamp(
            _configuration?.GetValue(
                "RasAutomation:FutureTimestampToleranceSeconds", 5) ?? 5,
            0,
            60));
        var relayReportTimeout = TimeSpan.FromSeconds(Math.Clamp(
            _configuration?.GetValue(
                "RasAutomation:RelayStateTimeoutSeconds", 30) ?? 30,
            5,
            300));

        foreach (var pump in pumps)
        {
            var rule = ReadAutoRule(pump.ParamDefaultsJson);
            if (rule is null)
            {
                SetAutomationStatus(pump, "unknown");
                continue;
            }

            var waterSystem = await _uow.WaterSystems.GetByIdAsync(
                pump.WaterSystemId, ct);
            if (waterSystem?.FarmingAreaId is not Guid areaId
                || device?.FarmingAreaId != areaId)
            {
                SetAutomationStatus(pump, "unsafe");
                continue;
            }
            if (!controllerReady)
            {
                SetAutomationStatus(pump, "unsafe");
                continue;
            }
            if (!firmwareInterlockVerified || !rule.FirmwareInterlock)
            {
                SetAutomationStatus(pump, "unsafe");
                continue;
            }

            var requiredCodes = new[] { rule.OnSensor, rule.OffSensor }
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var sensors = (await _uow.Sensors.FindAsync(
                    sensor => sensor.DeviceId == deviceId
                        && sensor.IsActive
                        && sensor.WaterSystemId == pump.WaterSystemId, ct))
                .ToList();
            var readings = new Dictionary<string, decimal>(
                StringComparer.OrdinalIgnoreCase);
            var evidenceInvalid = false;
            foreach (var code in requiredCodes)
            {
                var sensor = sensors.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.SensorCode, code,
                        StringComparison.OrdinalIgnoreCase));
                if (sensor is null)
                {
                    evidenceInvalid = true;
                    break;
                }
                var reading = (await _uow.WaterMeasurements.FindAsync(
                        measurement => measurement.SensorId == sensor.Id, ct))
                    .OrderByDescending(measurement => measurement.MeasuredAt)
                    .FirstOrDefault();
                var maxAge = SensorFreshness(sensor.SensorType);
                if (reading is null
                    || sensor.LastSeenAt is not DateTime sensorSeenAt
                    || sensorSeenAt > now.Add(futureTolerance)
                    || sensorSeenAt < now.Subtract(maxAge)
                    || reading.MeasuredAt > now.Add(futureTolerance)
                    || reading.MeasuredAt < now.Subtract(maxAge))
                {
                    evidenceInvalid = true;
                    break;
                }
                readings[code] = reading.Value;
            }
            if (evidenceInvalid || readings.Count != requiredCodes.Length)
            {
                SetAutomationStatus(pump, "unknown");
                continue;
            }

            var offTriggered = rule.OffSensor is not null
                && readings.TryGetValue(rule.OffSensor, out var offVal)
                && offVal == rule.OffValue;
            var onTriggered = rule.OnSensor is not null
                && readings.TryGetValue(rule.OnSensor, out var onVal)
                && onVal == rule.OnValue;
            if (offTriggered && onTriggered)
            {
                SetAutomationStatus(pump, "unsafe");
                continue;
            }

            bool? desired = offTriggered ? false : onTriggered ? true : null;
            if (!desired.HasValue) continue;
            var wantOn = desired.Value;
            var pending = await LatestRelayCommandAsync(
                device!.DeviceCode, pump.RelayChannel, ct);
            if (pending?.Status == "acknowledged"
                && (!pump.LastReportedAt.HasValue
                    || pump.LastReportedAt < pending.CreatedAt))
            {
                SetAutomationStatus(
                    pump,
                    now - pending.CreatedAt >= relayReportTimeout
                        ? "unsafe"
                        : "unknown");
                continue;
            }
            // Avoid queue flooding while the same target is still in flight.
            // A failed/expired request never changes the observed IsOn.
            if (pending is not null && pending.ExpiresAt > DateTime.UtcNow
                && pending.Status is "pending" or "delivered" or "acknowledged")
            {
                var pendingTarget = pending.Command == "on" ? true
                    : pending.Command is "off" or "alloff" ? false : (bool?)null;
                if (pendingTarget == wantOn) continue;
            }
            if (pump.LastReportedAt.HasValue && wantOn == pump.IsOn && pending is null) continue;
            if (pump.LastReportedAt.HasValue && wantOn == pump.IsOn && pending is not null
                && pending.Status is not ("pending" or "delivered")) continue;
            SetAutomationStatus(pump, "active");
            await EnqueueRelayAsync(pump, wantOn, null, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    private TimeSpan SensorFreshness(string sensorType)
    {
        var configured = _configuration?.GetValue<int?>(
            $"RasAutomation:SensorFreshnessSeconds:{sensorType}");
        var fallback = _configuration?.GetValue(
            "RasAutomation:DefaultSensorFreshnessSeconds", 30) ?? 30;
        return TimeSpan.FromSeconds(Math.Clamp(
            configured ?? fallback, 5, 3600));
    }

    private void SetAutomationStatus(
        RasComponent component,
        string status)
    {
        if (component.Status == status)
            return;
        component.Status = status;
        _uow.RasComponents.Update(component);
    }

    private async Task<EdgeCommand?> LatestRelayCommandAsync(
        string deviceCode, string? channel, CancellationToken ct)
    {
        return (await _uow.EdgeCommands.FindAsync(
                command => command.DeviceCode == deviceCode
                    && (command.Channel == channel || command.Command == "alloff"), ct))
            .OrderByDescending(command => command.CommandSequence)
            .ThenByDescending(command => command.CreatedAt)
            .ThenByDescending(command => command.Id)
            .FirstOrDefault();
    }

    private static string RelayCommandStatus(EdgeCommand command, RasComponent component)
    {
        if (command.Status is "pending" or "delivered")
            return command.ExpiresAt != default && command.ExpiresAt <= DateTime.UtcNow
                ? "timedout" : "pending";
        if (command.Status == "acknowledged")
        {
            bool? target = command.Command == "on" ? true
                : command.Command is "off" or "alloff" ? false : null;
            return target.HasValue && target.Value == component.IsOn
                && component.LastReportedAt >= command.CreatedAt
                ? "confirmed" : "awaiting_report";
        }
        return command.Status;
    }

    private async Task EnqueueRelayAsync(
        RasComponent node, bool desired, Guid? actorId, CancellationToken ct)
    {
        if (node.RelayDeviceId is not Guid deviceId)
            throw AppException.Conflict("Node chưa được gán controller.");
        var device = await _uow.Devices.GetByIdAsync(deviceId, ct)
            ?? throw AppException.NotFound("Controller");
        var request = new EnqueueEdgeCommandRequest(
            device.DeviceCode, desired ? "on" : "off", node.RelayChannel);
        if (actorId is Guid userId && userId != Guid.Empty)
        {
            await _edgeCommands.EnqueueManualAsync(request, userId, ct);
            return;
        }

        var waterSystem = await _uow.WaterSystems.GetByIdAsync(node.WaterSystemId, ct)
            ?? throw AppException.NotFound("WaterSystem");
        if (waterSystem.FarmingAreaId is not Guid areaId || areaId == Guid.Empty)
            throw AppException.Forbidden("Automation không xác định được khu của relay.");
        await _edgeCommands.EnqueueAutomationAsync(
            request, areaId, "ras-auto-relay", ct);
    }

    private sealed record AutoRule(
        string? OnSensor,
        decimal OnValue,
        string? OffSensor,
        decimal OffValue,
        bool FirmwareInterlock);

    private static AutoRule? ReadAutoRule(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("auto", out var nested) && nested.ValueKind == JsonValueKind.Object)
                root = nested;
            return new AutoRule(
                ReadString(root, "onSensor"),
                ReadDecimal(root, "onValue", 1),
                ReadString(root, "offSensor"),
                ReadDecimal(root, "offValue", 1),
                ReadBool(root, "firmwareInterlock", false));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static decimal ReadDecimal(JsonElement root, string name, decimal fallback)
    {
        if (!root.TryGetProperty(name, out var el))
            return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(el.GetString(), out var n) => n,
            _ => fallback
        };
    }

    private static bool ReadBool(JsonElement root, string name, bool fallback)
    {
        if (!root.TryGetProperty(name, out var el))
            return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var value)
                => value,
            _ => fallback
        };
    }

    private static RasComponentDto MapComponent(RasComponent c) =>
        new(c.Id, c.WaterSystemId, c.Code, c.Name, c.Type, c.Status, c.Position, c.Capacity,
            c.Description, c.NodeType, c.IconKey, c.RelayDeviceId, c.RelayChannel,
            c.ParamDefaultsJson, c.HasRelay,
            c.LastReportedAt.HasValue ? c.IsOn : null, c.ControlMode, c.LastReportedAt);

    private static WaterFlowDto MapFlow(WaterFlow f) =>
        new(f.Id, f.WaterSystemId, f.FromComponentId, f.ToComponentId, f.FlowRate, f.Status, f.SortOrder);
}
