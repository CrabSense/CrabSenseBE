namespace CrabSenseBE.Application.DTOs.IoT;

public record RasComponentDto(
    Guid Id,
    Guid WaterSystemId,
    string Code,
    string Name,
    string Type,
    string Status,
    int Position,
    decimal? Capacity,
    string? Description,
    string? NodeType,
    string? IconKey,
    Guid? RelayDeviceId,
    string? RelayChannel,
    string? ParamDefaultsJson,
    bool HasRelay,
    bool IsOn,
    string? ControlMode);

public record WaterFlowDto(
    Guid Id,
    Guid WaterSystemId,
    Guid FromComponentId,
    Guid ToComponentId,
    decimal? FlowRate,
    string Status,
    int SortOrder);

public record RasFlowNodeDto(
    Guid Id,
    string NodeCode,
    string DisplayLabel,
    int SortOrder,
    string NodeType,
    string? RelayDeviceId,
    string? RelayChannel,
    string? ParamDefaultsJson,
    string? IconKey,
    string MetricLabel,
    string? SecondaryMetric,
    bool HasRelay,
    bool? IsOn,
    bool? IsOnline,
    string ConnectionLabel,
    string? ControlMode,
    string? AlertMessage,
    decimal? PowerW,
    decimal? CurrentA,
    decimal? VoltageV,
    decimal? FlowLpm,
    decimal? FlowLph,
    decimal? TempC,
    decimal? LevelPercent,
    string Type,
    string Status,
    decimal? Capacity);

public record RasFlowDiagramDto(
    Guid AreaId,
    string AreaCode,
    string AreaName,
    Guid WaterSystemId,
    string WaterSystemName,
    string? FlowStatus,
    IReadOnlyList<RasFlowNodeDto> Nodes,
    IReadOnlyList<WaterFlowDto> Flows,
    decimal TotalPowerW,
    int RunningCount,
    int ControllableCount,
    int OnlineCount);

public record CreateRasFlowNodeRequest(
    string NodeCode,
    string DisplayLabel,
    int SortOrder = 0,
    string? NodeType = "equipment",
    string? Type = null,
    string? RelayChannel = null,
    Guid? RelayDeviceId = null,
    object? ParamDefaults = null,
    string? ParamDefaultsJson = null,
    decimal? Capacity = null,
    string? Description = null);

public record ReorderRasFlowRequest(IReadOnlyList<Guid> NodeIds);

public record RasFlowCommandRequest(string Command);
