using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>Resolve unambiguous configured area/row, not physical installation.</summary>
public sealed record SensorHistoryLocation(Guid AreaId, Guid? RowId);

public static class SensorHistoryScope
{
    public static async Task<SensorHistoryLocation?> ResolveAsync(IUnitOfWork uow,Sensor sensor,Device? device,CancellationToken ct)
    {
        if(sensor.DeviceId.HasValue && (device is null || device.Id!=sensor.DeviceId.Value))return null;
        if(!sensor.DeviceId.HasValue && device is not null)return null;
        var areas=new HashSet<Guid>();
        var rows=new HashSet<Guid>();
        var systems=new HashSet<Guid>();
        if(sensor.FarmingAreaId is Guid sa)areas.Add(sa);
        if(device?.FarmingAreaId is Guid da)areas.Add(da);
        if(sensor.FarmingRowId is Guid sr)rows.Add(sr);
        if(device?.FarmingRowId is Guid dr)rows.Add(dr);
        if(sensor.WaterSystemId is Guid ws)systems.Add(ws);
        if(sensor.RasComponentId is Guid componentId)
        {
            var component=await uow.RasComponents.GetByIdAsync(componentId,ct);
            if(component is null)return null;
            systems.Add(component.WaterSystemId);
        }
        if(rows.Count>1||systems.Count>1)return null;
        foreach(var id in rows)
        {
            var row=await uow.FarmingRows.GetByIdAsync(id,ct);
            if(row is null)return null;
            areas.Add(row.FarmingAreaId);
        }
        foreach(var id in systems)
        {
            var system=await uow.WaterSystems.GetByIdAsync(id,ct);
            if(system is null || system.FarmingAreaId is not Guid area)return null;
            areas.Add(area);
        }
        if(areas.Count!=1||areas.Contains(Guid.Empty)||rows.Contains(Guid.Empty))return null;
        return new SensorHistoryLocation(areas.Single(),rows.Count==0?null:rows.Single());
    }
}
