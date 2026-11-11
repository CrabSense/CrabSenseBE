using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

internal sealed class MediaAssetAreaGuard
{
    private readonly IUnitOfWork _uow;
    private readonly FarmAreaScope _scope;

    public MediaAssetAreaGuard(IUnitOfWork uow, ICurrentUserService? currentUser)
    {
        _uow = uow;
        _scope = new FarmAreaScope(uow, currentUser);
    }

    public async Task EnsureAsync(MediaAsset asset, CancellationToken ct)
    {
        var allowed = await _scope.AreaIdsAsync(ct);
        if (allowed is null) return;
        var required = await AreaIdsAsync(asset, ct);
        if (required.Count == 0 || required.Any(id => !allowed.Contains(id)))
            throw AppException.Forbidden("Bạn không có quyền truy cập media ngoài khu được phân công.");
    }

    public async Task<bool> CanAccessAsync(MediaAsset asset, CancellationToken ct)
    {
        try
        {
            await EnsureAsync(asset, ct);
            return true;
        }
        catch (AppException)
        {
            return false;
        }
    }

    private async Task<HashSet<Guid>> AreaIdsAsync(MediaAsset asset, CancellationToken ct)
    {
        if (asset.BoxId is Guid boxId)
            return [await AreaOfBoxAsync(boxId, ct)];
        if (asset.CrabId is Guid crabId)
            return await AreaOfCrabAsync(crabId, ct);
        if (asset.DeviceId is Guid deviceId)
            return await AreaOfDeviceAsync(deviceId, ct);

        var type = asset.RelatedEntityType?.Trim().ToLowerInvariant();
        var id = asset.RelatedEntityId;
        if (id is null) return [];
        return type switch
        {
            "box" => [await AreaOfBoxAsync(id.Value, ct)],
            "crab" => await AreaOfCrabAsync(id.Value, ct),
            "crablot" or "lot" => await AreaOfLotAsync(id.Value, ct),
            "harvestvoucher" or "harvest" => await AreaOfHarvestAsync(id.Value, ct),
            "frozenlot" => await AreaOfFrozenLotAsync(id.Value, ct),
            "device" => await AreaOfDeviceAsync(id.Value, ct),
            _ => []
        };
    }

    private async Task<Guid> AreaOfBoxAsync(Guid id, CancellationToken ct)
    {
        var box = await _uow.Boxes.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Box");
        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct)
            ?? throw AppException.NotFound("FarmingRow");
        return row.FarmingAreaId;
    }

    private async Task<HashSet<Guid>> AreaOfCrabAsync(Guid id, CancellationToken ct)
    {
        var crab = await _uow.Crabs.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Crab");
        if (crab.BoxId is Guid boxId) return [await AreaOfBoxAsync(boxId, ct)];
        var allocation = (await _uow.CrabBoxAllocations.FindAsync(a => a.CrabId == id, ct))
            .OrderByDescending(a => a.StartTime).FirstOrDefault();
        if (allocation is not null) return [await AreaOfBoxAsync(allocation.BoxId, ct)];
        return await AreaOfLotAsync(crab.CrabLotId, ct);
    }

    private async Task<HashSet<Guid>> AreaOfLotAsync(Guid id, CancellationToken ct)
    {
        _ = await _uow.CrabLots.GetByIdAsync(id, ct) ?? throw AppException.NotFound("CrabLot");
        return (await _uow.CrabLotAreas.FindAsync(a => a.CrabLotId == id, ct))
            .Select(a => a.FarmingAreaId).ToHashSet();
    }

    private async Task<HashSet<Guid>> AreaOfHarvestAsync(Guid id, CancellationToken ct)
    {
        var voucher = await _uow.HarvestVouchers.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("HarvestVoucher");
        if (voucher.FarmingAreaId is Guid areaId) return [areaId];
        return (await _uow.HarvestApprovedLines.FindAsync(a => a.HarvestVoucherId == id, ct))
            .Select(a => a.FarmingAreaId).ToHashSet();
    }

    private async Task<HashSet<Guid>> AreaOfFrozenLotAsync(Guid id, CancellationToken ct)
    {
        var lot = await _uow.FrozenLots.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("FrozenLot");
        return lot.FarmingAreaId is Guid areaId ? [areaId] : [];
    }

    private async Task<HashSet<Guid>> AreaOfDeviceAsync(Guid id, CancellationToken ct)
    {
        var device = await _uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Device");
        if (device.FarmingAreaId is Guid areaId) return [areaId];
        if (device.FarmingRowId is not Guid rowId) return [];
        var row = await _uow.FarmingRows.GetByIdAsync(rowId, ct) ?? throw AppException.NotFound("FarmingRow");
        return [row.FarmingAreaId];
    }
}
