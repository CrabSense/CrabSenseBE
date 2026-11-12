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
        if (asset.RelatedEntityType == "crab-pending" && !asset.RelatedEntityId.HasValue
            && !asset.CrabId.HasValue && !asset.BoxId.HasValue && !asset.DeviceId.HasValue)
        {
            var actor = await _scope.CurrentUserAsync(ct);
            if (actor is null || actor.Id != asset.UploadedBy
                || actor.Role == CrabSenseBE.Domain.Enums.UserRole.SystemAdministrator)
                throw AppException.Forbidden("Ảnh chờ đăng ký chỉ thuộc người đã tải lên.");
            return;
        }
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
        var required = new HashSet<Guid>();
        var referenceScopes = new List<HashSet<Guid>>();
        if (asset.BoxId is Guid boxId)
        {
            var scope = new HashSet<Guid> { await AreaOfBoxAsync(boxId, ct) };
            referenceScopes.Add(scope);
            required.UnionWith(scope);
        }
        if (asset.CrabId is Guid crabId)
        {
            var scope = await AreaOfCrabAsync(crabId, ct);
            referenceScopes.Add(scope);
            required.UnionWith(scope);
        }
        if (asset.DeviceId is Guid deviceId)
        {
            var scope = await AreaOfDeviceAsync(deviceId, ct);
            referenceScopes.Add(scope);
            required.UnionWith(scope);
        }

        if (asset.BoxId is Guid linkedBoxId && asset.CrabId is Guid linkedCrabId)
            await EnsureCrabBelongsToBoxAsync(linkedCrabId, linkedBoxId, ct);

        var type = asset.RelatedEntityType?.Trim().ToLowerInvariant();
        var id = asset.RelatedEntityId;
        if (id is null)
        {
            if (!string.IsNullOrWhiteSpace(type))
                throw AppException.BadRequest("RelatedEntityId is required.");
            EnsureCoherentScopes(referenceScopes);
            return required;
        }
        if (string.IsNullOrWhiteSpace(type))
            throw AppException.BadRequest("RelatedEntityType is required.");

        HashSet<Guid> relatedAreas;
        switch (type)
        {
            case "box":
                if (asset.BoxId is Guid explicitBoxId && explicitBoxId != id.Value)
                    throw AppException.BadRequest("BoxId conflicts with RelatedEntityId.");
                relatedAreas = [await AreaOfBoxAsync(id.Value, ct)];
                break;
            case "crab":
                if (asset.CrabId is Guid explicitCrabId && explicitCrabId != id.Value)
                    throw AppException.BadRequest("CrabId conflicts with RelatedEntityId.");
                relatedAreas = await AreaOfCrabAsync(id.Value, ct);
                break;
            case "device":
                if (asset.DeviceId is Guid explicitDeviceId && explicitDeviceId != id.Value)
                    throw AppException.BadRequest("DeviceId conflicts with RelatedEntityId.");
                relatedAreas = await AreaOfDeviceAsync(id.Value, ct);
                break;
            case "crablot":
            case "lot":
                relatedAreas = await AreaOfLotAsync(id.Value, ct);
                break;
            case "harvestvoucher":
            case "harvest":
                relatedAreas = await AreaOfHarvestAsync(id.Value, ct);
                break;
            case "frozenlot":
                relatedAreas = await AreaOfFrozenLotAsync(id.Value, ct);
                break;
            default:
                throw AppException.BadRequest(
                    $"RelatedEntityType '{asset.RelatedEntityType}' is not supported.");
        }

        referenceScopes.Add(relatedAreas);
        EnsureCoherentScopes(referenceScopes);
        required.UnionWith(relatedAreas);
        return required;
    }

    private static void EnsureCoherentScopes(IReadOnlyList<HashSet<Guid>> scopes)
    {
        if (scopes.Count < 2) return;
        var common = new HashSet<Guid>(scopes[0]);
        foreach (var scope in scopes.Skip(1))
            common.IntersectWith(scope);
        if (common.Count == 0)
            throw AppException.BadRequest(
                "Các liên kết media không thuộc cùng phạm vi khu.");
    }

    private async Task EnsureCrabBelongsToBoxAsync(
        Guid crabId,
        Guid boxId,
        CancellationToken ct)
    {
        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct)
            ?? throw AppException.NotFound("Crab");
        if (crab.BoxId == boxId) return;
        var allocation = await _uow.CrabBoxAllocations.FirstOrDefaultAsync(
            item => item.CrabId == crabId && item.BoxId == boxId, ct);
        if (allocation is null)
            throw AppException.BadRequest("CrabId is not related to BoxId.");
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
