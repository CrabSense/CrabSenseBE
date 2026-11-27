using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>Resolve unambiguous configured area/row, not physical installation.</summary>
public sealed record SensorHistoryLocation(Guid AreaId, Guid? RowId);

public static class SensorHistoryScope
{
    public static Task<SensorHistoryLocation?> ResolveAsync(IUnitOfWork uow,Sensor sensor,Device? device,CancellationToken ct)
        => ResolveCoreAsync(sensor,device,
            async id=>(await uow.FarmingRows.GetByIdAsync(id,ct))?.FarmingAreaId,
            async id=>(await uow.WaterSystems.GetByIdAsync(id,ct))?.FarmingAreaId,
            async id=>(await uow.RasComponents.GetByIdAsync(id,ct))?.WaterSystemId);

    // Live list already loads these lookup tables. Use the same scope policy
    // as ingest/history without one database round trip per sensor.
    public static Task<SensorHistoryLocation?> ResolveFromSnapshotAsync(Sensor sensor,Device? device,
        Func<Guid,Guid?> rowArea,Func<Guid,Guid?> systemArea,Func<Guid,Guid?> componentSystem)
        => ResolveCoreAsync(sensor,device,id=>Task.FromResult(rowArea(id)),
            id=>Task.FromResult(systemArea(id)),id=>Task.FromResult(componentSystem(id)));

    private static async Task<SensorHistoryLocation?> ResolveCoreAsync(Sensor sensor,Device? device,
        Func<Guid,Task<Guid?>> rowArea,Func<Guid,Task<Guid?>> systemArea,Func<Guid,Task<Guid?>> componentSystem)
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
            if(await componentSystem(componentId) is not Guid systemId)return null;
            systems.Add(systemId);
        }
        if(rows.Count>1||systems.Count>1)return null;
        foreach(var id in rows)
        {
            if(await rowArea(id) is not Guid area)return null;
            areas.Add(area);
        }
        foreach(var id in systems)
        {
            if(await systemArea(id) is not Guid area)return null;
            areas.Add(area);
        }
        if(areas.Count!=1||areas.Contains(Guid.Empty)||rows.Contains(Guid.Empty))return null;
        return new SensorHistoryLocation(areas.Single(),rows.Count==0?null:rows.Single());
    }
}
