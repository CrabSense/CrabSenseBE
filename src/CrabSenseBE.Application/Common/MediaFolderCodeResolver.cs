using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Common;

/// <summary>
/// Path Drive theo cây trại: khu / dãy / hộp / cua.
/// </summary>
public static class MediaFolderCodeResolver
{
    public static async Task<string> ResolvePathAsync(
        IUnitOfWork uow, string? entityType, Guid? entityId, CancellationToken ct = default)
    {
        if (entityId is not Guid id || id == Guid.Empty)
            return Fallback(entityType, null, null);

        var table = MediaFolderPath.MapTable(entityType);
        return table switch
        {
            "Crabs" => await CrabPathAsync(uow, id, ct),
            "CrabFeedings" => MediaFolderPath.Join(
                await CrabPathAsync(uow, id, ct),
                MediaFolderPath.FeedingFolder),
            var t when t == MediaFolderPath.InboundRoot => await LotPathAsync(uow, id, ct),
            "FarmingAreas" => await AreaPathAsync(uow, id, ct),
            "FarmingRows" => await RowPathAsync(uow, id, ct),
            "Boxes" => await BoxPathAsync(uow, id, ct),
            var t when t == MediaFolderPath.DeviceFolder => await DevicePathAsync(uow, id, ct),
            var t when t == MediaFolderPath.HarvestFolder => await HarvestPathAsync(uow, entityType, id, ct),
            var t when t == MediaFolderPath.FrozenRoot => await FrozenPathAsync(uow, id, ct),
            var t when t == MediaFolderPath.UsersRoot => UserPath(await uow.Users.GetByIdAsync(id, ct), id),
            var t when t == MediaFolderPath.OpsFolder => await OperationPathAsync(uow, id, ct),
            "Inspections" => await InspectionPathAsync(uow, id, ct),
            var t when t == MediaFolderPath.WaterFolder => await WaterPathAsync(uow, id, ct),
            _ => Fallback(entityType, id, null)
        };
    }

    /// <summary>Giữ API cũ: chỉ trả mã lá (không path). Prefer <see cref="ResolvePathAsync"/>.</summary>
    public static async Task<string?> LookupAsync(
        IUnitOfWork uow, string? entityType, Guid? entityId, CancellationToken ct = default)
    {
        var path = await ResolvePathAsync(uow, entityType, entityId, ct);
        var leaf = path.Split('/').LastOrDefault();
        return string.IsNullOrWhiteSpace(leaf) || leaf == MediaFolderPath.PendingSegment ? null : leaf;
    }

    private static async Task<string> CrabPathAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var crab = await uow.Crabs.GetByIdAsync(id, ct);
        if (crab is null)
            return MediaFolderPath.Join(MediaFolderPath.InboundRoot, MediaFolderPath.PendingSegment);

        var leaf = MediaFolderPath.Leaf(crab.Id, crab.Code);
        if (crab.BoxId is Guid boxId && boxId != Guid.Empty)
        {
            var branch = await FarmBranchAsync(uow, boxId, null, null, ct);
            if (branch.Length > 0)
                return MediaFolderPath.Join(MediaFolderPath.Join(branch), leaf);
        }

        var lot = crab.CrabLotId != Guid.Empty ? await uow.CrabLots.GetByIdAsync(crab.CrabLotId, ct) : null;
        var lotCode = NullIfBlank(lot?.LotCode) ?? MediaFolderPath.PendingSegment;
        return MediaFolderPath.Join(MediaFolderPath.InboundRoot, lotCode, leaf);
    }

    private static async Task<string> LotPathAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var lot = await uow.CrabLots.GetByIdAsync(id, ct);
        return MediaFolderPath.Join(MediaFolderPath.InboundRoot, MediaFolderPath.Leaf(id, lot?.LotCode));
    }

    private static async Task<string> AreaPathAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var area = await uow.FarmingAreas.GetByIdAsync(id, ct);
        return MediaFolderPath.Join(MediaFolderPath.Leaf(id, area?.Code), MediaFolderPath.AreasLeaf);
    }

    private static async Task<string> RowPathAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var row = await uow.FarmingRows.GetByIdAsync(id, ct);
        var branch = await FarmBranchAsync(uow, null, id, row?.FarmingAreaId, ct);
        return branch.Length > 0
            ? MediaFolderPath.Join(branch)
            : MediaFolderPath.Join("FarmingRows", MediaFolderPath.Leaf(id, row?.Code));
    }

    private static async Task<string> BoxPathAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var box = await uow.Boxes.GetByIdAsync(id, ct);
        var branch = await FarmBranchAsync(uow, id, box?.FarmingRowId, null, ct);
        return branch.Length > 0
            ? MediaFolderPath.Join(branch)
            : MediaFolderPath.Join("Boxes", MediaFolderPath.Leaf(id, box?.Code));
    }

    private static async Task<string> DevicePathAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var device = await uow.Devices.GetByIdAsync(id, ct);
        var branch = await FarmBranchAsync(uow, null, device?.FarmingRowId, device?.FarmingAreaId, ct);
        var leaf = MediaFolderPath.Leaf(id, device?.DeviceCode);
        return branch.Length > 0
            ? MediaFolderPath.Join(MediaFolderPath.Join(branch), MediaFolderPath.DeviceFolder, leaf)
            : MediaFolderPath.Join(MediaFolderPath.DeviceFolder, leaf);
    }

    private static async Task<string> HarvestPathAsync(IUnitOfWork uow, string? entityType, Guid id, CancellationToken ct)
    {
        if (IsHarvestLine(entityType))
        {
            var line = await uow.HarvestLines.GetByIdAsync(id, ct);
            if (line?.CrabId is Guid crabId)
                return await CrabPathAsync(uow, crabId, ct);
            if (line?.BoxId is Guid boxId)
                return await BoxPathAsync(uow, boxId, ct);
            if (line is not null)
                return await HarvestVoucherPathAsync(uow, line.HarvestVoucherId, ct);
        }

        return await HarvestVoucherPathAsync(uow, id, ct);
    }

    private static bool IsHarvestLine(string? entityType)
    {
        var n = new string((entityType ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return n is "harvestline" or "harvestlines";
    }

    private static async Task<string> HarvestVoucherPathAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var voucher = await uow.HarvestVouchers.GetByIdAsync(id, ct);
        var branch = await FarmBranchAsync(uow, null, null, voucher?.FarmingAreaId, ct);
        var leaf = MediaFolderPath.Leaf(id, voucher?.VoucherCode);
        return branch.Length > 0
            ? MediaFolderPath.Join(MediaFolderPath.Join(branch), MediaFolderPath.HarvestFolder, leaf)
            : MediaFolderPath.Join(MediaFolderPath.HarvestFolder, leaf);
    }

    private static async Task<string> FrozenPathAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var lot = await uow.FrozenLots.GetByIdAsync(id, ct);
        return MediaFolderPath.Join(MediaFolderPath.FrozenRoot, MediaFolderPath.Leaf(id, lot?.LotCode));
    }

    private static string UserPath(AppUser? user, Guid id)
    {
        var name = NullIfBlank(user?.Username) ?? NullIfBlank(user?.EmployeeId);
        if (name is not null) name = name.Replace('@', '_');
        return MediaFolderPath.Join(MediaFolderPath.UsersRoot, MediaFolderPath.Leaf(id, name));
    }

    private static async Task<string> OperationPathAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var op = await uow.FarmOperations.GetByIdAsync(id, ct);
        var leaf = op is null
            ? MediaFolderPath.Leaf(id, null)
            : $"{(string.IsNullOrWhiteSpace(op.Type) ? "op" : op.Type.Trim())}_{op.Timestamp:yyyyMMdd}_{op.Id.ToString("N")[..8]}";

        Guid? boxId = null;
        foreach (var raw in JsonStringList.Parse(op?.BoxIdsJson))
        {
            if (Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty)
            {
                boxId = parsed;
                break;
            }
        }

        var branch = await FarmBranchAsync(uow, boxId, null, null, ct);
        return branch.Length > 0
            ? MediaFolderPath.Join(MediaFolderPath.Join(branch), MediaFolderPath.OpsFolder, leaf)
            : MediaFolderPath.Join(MediaFolderPath.OpsFolder, leaf);
    }

    private static async Task<string> InspectionPathAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var insp = await uow.Inspections.GetByIdAsync(id, ct);
        if (insp?.CrabId is Guid crabId)
            return await CrabPathAsync(uow, crabId, ct);
        if (insp?.BoxId is Guid boxId)
            return await BoxPathAsync(uow, boxId, ct);
        return MediaFolderPath.Join("Inspections", MediaFolderPath.Leaf(id, null));
    }

    private static async Task<string> WaterPathAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var run = await uow.WaterAnalysisRuns.GetByIdAsync(id, ct);
        var branch = await FarmBranchAsync(uow, null, null, run?.FarmingAreaId, ct);
        var day = (run?.StartedAt ?? DateTime.UtcNow).ToString("yyyyMMdd");
        return branch.Length > 0
            ? MediaFolderPath.Join(MediaFolderPath.Join(branch), MediaFolderPath.WaterFolder, day)
            : MediaFolderPath.Join(MediaFolderPath.WaterFolder, day);
    }

    private static async Task<string[]> FarmBranchAsync(
        IUnitOfWork uow, Guid? boxId, Guid? rowId, Guid? areaId, CancellationToken ct)
    {
        string? box = null, row = null, area = null;

        if (boxId is Guid bid && bid != Guid.Empty)
        {
            var b = await uow.Boxes.GetByIdAsync(bid, ct);
            if (b is not null)
            {
                box = NullIfBlank(b.Code) ?? bid.ToString("D");
                rowId ??= b.FarmingRowId;
            }
        }

        if (rowId is Guid rid && rid != Guid.Empty)
        {
            var r = await uow.FarmingRows.GetByIdAsync(rid, ct);
            if (r is not null)
            {
                row = NullIfBlank(r.Code) ?? rid.ToString("D");
                areaId ??= r.FarmingAreaId;
            }
        }

        if (areaId is Guid aid && aid != Guid.Empty)
        {
            var a = await uow.FarmingAreas.GetByIdAsync(aid, ct);
            if (a is not null)
                area = NullIfBlank(a.Code) ?? aid.ToString("D");
        }

        return new[] { area, row, box }.Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>().ToArray();
    }

    private static string Fallback(string? entityType, Guid? id, string? code)
        => MediaFolderPath.Build(entityType, id, folderName: code);

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
