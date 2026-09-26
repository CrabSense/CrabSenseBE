using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class RasFlowService : IRasFlowService
{
    private readonly IUnitOfWork _uow;
    private readonly IEdgeCommandService _edgeCommands;

    public RasFlowService(IUnitOfWork uow, IEdgeCommandService edgeCommands)
    {
        _uow = uow;
        _edgeCommands = edgeCommands;
    }

    public async Task<ApiResponse<RasFlowDiagramDto>> GetDiagramByAreaAsync(Guid areaId, CancellationToken ct = default)
    {
        var area = await RequireAreaAsync(areaId, ct);
        var ws = await EnsureSystemAsync(area, ct);
        await EnsureDefaultPipelineAsync(ws, ct);
        return ApiResponse<RasFlowDiagramDto>.Ok(await BuildDiagramAsync(area, ws, ct));
    }

    public async Task<ApiResponse<RasFlowDiagramDto>> AddNodeAsync(
        Guid areaId, CreateRasFlowNodeRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.NodeCode))
            throw AppException.BadRequest("nodeCode is required.");
        if (string.IsNullOrWhiteSpace(req.DisplayLabel))
            throw AppException.BadRequest("displayLabel is required.");

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

        var prev = existing.OrderBy(c => c.Position).LastOrDefault(c => c.Position < node.Position)
                   ?? existing.OrderBy(c => c.Position).LastOrDefault();
        if (prev is not null)
            await AddFlowIfMissingAsync(ws.Id, prev.Id, node.Id, existing.Count, ct);

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<RasFlowDiagramDto>.Ok(await BuildDiagramAsync(area, ws, ct), "Node added.");
    }

    public async Task<ApiResponse<RasFlowDiagramDto>> ReorderAsync(
        Guid areaId, ReorderRasFlowRequest req, CancellationToken ct = default)
    {
        if (req.NodeIds is null || req.NodeIds.Count == 0)
            throw AppException.BadRequest("nodeIds is required.");

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

        var oldFlows = (await _uow.WaterFlows.FindAsync(f => f.WaterSystemId == ws.Id, ct)).ToList();
        foreach (var f in oldFlows) _uow.WaterFlows.Remove(f);

        for (var i = 0; i < req.NodeIds.Count - 1; i++)
        {
            await _uow.WaterFlows.AddAsync(new WaterFlow
            {
                WaterSystemId = ws.Id,
                FromComponentId = req.NodeIds[i],
                ToComponentId = req.NodeIds[i + 1],
                Status = "active",
                SortOrder = i
            }, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<RasFlowDiagramDto>.Ok(await BuildDiagramAsync(area, ws, ct), "Reordered.");
    }

    public async Task<ApiResponse> DeleteNodeAsync(Guid areaId, Guid nodeId, CancellationToken ct = default)
    {
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
        var area = await RequireAreaAsync(areaId, ct);
        var ws = await EnsureSystemAsync(area, ct);
        var node = await _uow.RasComponents.GetByIdAsync(nodeId, ct)
            ?? throw AppException.NotFound("RasComponent");
        if (node.WaterSystemId != ws.Id)
            throw AppException.BadRequest("Node does not belong to this area RAS.");
        if (!node.HasRelay)
            throw AppException.BadRequest("Node has no relay.");

        var cmd = (req.Command ?? "").Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
        var title = $"{node.Name}: {cmd}";
        if (cmd is "auto")
        {
            node.ControlMode = "auto";
            title = $"Auto kích hoạt {node.Name}";
        }
        else if (cmd is "manual")
        {
            node.ControlMode = "manual";
            title = $"Chuyển {node.Name} sang Manual";
        }
        else if (cmd is "on" or "start" or "open")
        {
            node.ControlMode = "manual";
            if (!node.IsOn) node.RunStartedAt = now;
            node.IsOn = true;
            title = $"Bật {node.Name}";
        }
        else if (cmd is "off" or "stop" or "close")
        {
            node.ControlMode = "manual";
            node.IsOn = false;
            node.RunStartedAt = null;
            title = $"Tắt {node.Name}";
        }
        else if (cmd == "toggle")
        {
            node.ControlMode = "manual";
            node.IsOn = !node.IsOn;
            node.RunStartedAt = node.IsOn ? now : null;
            title = node.IsOn ? $"Bật {node.Name}" : $"Tắt {node.Name}";
        }
        else
            throw AppException.BadRequest("Command must be auto, manual, on, off, start, stop, or toggle.");

        node.LastCommandAt = now;
        _uow.RasComponents.Update(node);
        if (actorId is Guid uid && uid != Guid.Empty)
        {
            await _uow.OperationLogs.AddAsync(new OperationLog
            {
                UserId = uid,
                Action = cmd,
                EntityType = "RasComponent",
                EntityId = node.Id,
                Details = $"{area.Id:N}|{title}"
            }, ct);
        }
        await _uow.SaveChangesAsync(ct);
        if (node.RelayDeviceId is Guid deviceId
            && cmd is ("on" or "off" or "start" or "stop" or "open" or "close" or "toggle"))
        {
            var device = await _uow.Devices.GetByIdAsync(deviceId, ct);
            if (device is not null)
            {
                await _edgeCommands.EnqueueAsync(
                    new EnqueueEdgeCommandRequest(
                        device.DeviceCode,
                        node.IsOn ? "on" : "off",
                        node.RelayChannel),
                    actorId,
                    ct);
            }
        }
        return ApiResponse<RasFlowDiagramDto>.Ok(await BuildDiagramAsync(area, ws, ct), "Command applied.");
    }

    public async Task<ApiResponse<IReadOnlyList<RasComponentDto>>> ListComponentsAsync(
        Guid waterSystemId, CancellationToken ct = default)
    {
        _ = await _uow.WaterSystems.GetByIdAsync(waterSystemId, ct) ?? throw AppException.NotFound("WaterSystem");
        var list = (await _uow.RasComponents.FindAsync(c => c.WaterSystemId == waterSystemId, ct))
            .OrderBy(c => c.Position)
            .Select(MapComponent)
            .ToList();
        return ApiResponse<IReadOnlyList<RasComponentDto>>.Ok(list);
    }

    public async Task<ApiResponse<IReadOnlyList<WaterFlowDto>>> ListFlowsAsync(
        Guid waterSystemId, CancellationToken ct = default)
    {
        _ = await _uow.WaterSystems.GetByIdAsync(waterSystemId, ct) ?? throw AppException.NotFound("WaterSystem");
        var list = (await _uow.WaterFlows.FindAsync(f => f.WaterSystemId == waterSystemId, ct))
            .OrderBy(f => f.SortOrder)
            .Select(MapFlow)
            .ToList();
        return ApiResponse<IReadOnlyList<WaterFlowDto>>.Ok(list);
    }

    private async Task<FarmingArea> RequireAreaAsync(Guid areaId, CancellationToken ct)
        => await _uow.FarmingAreas.GetByIdAsync(areaId, ct) ?? throw AppException.NotFound("FarmingArea");

    private async Task<WaterSystem> EnsureSystemAsync(FarmingArea area, CancellationToken ct)
    {
        var list = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == area.Id, ct)).ToList();
        var ws = list.FirstOrDefault(w => string.Equals(w.Type, "RAS", StringComparison.OrdinalIgnoreCase))
                 ?? list.FirstOrDefault();
        if (ws is not null) return ws;

        ws = new WaterSystem
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

    private async Task AddFlowIfMissingAsync(
        Guid wsId, Guid fromId, Guid toId, int sortOrder, CancellationToken ct)
    {
        var exists = await _uow.WaterFlows.AnyAsync(
            f => f.WaterSystemId == wsId && f.FromComponentId == fromId && f.ToComponentId == toId, ct);
        if (exists) return;
        await _uow.WaterFlows.AddAsync(new WaterFlow
        {
            WaterSystemId = wsId,
            FromComponentId = fromId,
            ToComponentId = toId,
            Status = "active",
            SortOrder = sortOrder
        }, ct);
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

        var nodes = comps.Select(c => MapNode(c, sensors, latest, flows)).ToList();
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
            nodes.Count(n => n.HasRelay && n.IsOn != true),
            nodes.Count(n => n.Status is "alarm" or "error" || n.IsOnline == false),
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
            c.HasRelay ? c.IsOn : null,
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

    private static RasComponentDto MapComponent(RasComponent c) =>
        new(c.Id, c.WaterSystemId, c.Code, c.Name, c.Type, c.Status, c.Position, c.Capacity,
            c.Description, c.NodeType, c.IconKey, c.RelayDeviceId, c.RelayChannel,
            c.ParamDefaultsJson, c.HasRelay, c.IsOn, c.ControlMode);

    private static WaterFlowDto MapFlow(WaterFlow f) =>
        new(f.Id, f.WaterSystemId, f.FromComponentId, f.ToComponentId, f.FlowRate, f.Status, f.SortOrder);
}
