using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.DTOs.Media;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>Phiếu nhập lô cua. Số cua đã thả (PlacedCount) tách khỏi Quantity khai báo.</summary>
public class FarmLotService : IFarmLotService
{
    private readonly IUnitOfWork _uow;
    private readonly IPublicImageStorage _images;
    private readonly FarmAreaScope _areas;

    public FarmLotService(IUnitOfWork uow, IPublicImageStorage images, ICurrentUserService? currentUser)
    {
        _uow = uow;
        _images = images;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public async Task<ApiResponse<IEnumerable<CrabLotDto>>> GetLotsAsync(CancellationToken ct = default)
    {
        var lots = (await _uow.CrabLots.GetAllAsync(ct)).ToList();
        var links = await LoadLinksAsync(ct);
        var scope = await _areas.AreaIdsAsync(ct);
        if (scope is not null)
            lots = lots.Where(lot => LinksOverlap(links, lot.Id, scope)).ToList();
        var placed = await LoadPlacedCountsAsync(scope, ct);
        return ApiResponse<IEnumerable<CrabLotDto>>.Ok(
            lots.OrderByDescending(l => l.ImportDate)
                .Select(l => MapLot(l, placed, links, scope)));
    }

    public async Task<ApiResponse<CrabLotDto>> GetLotByIdAsync(Guid id, CancellationToken ct = default)
    {
        var lot = await _uow.CrabLots.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("CrabLot");
        var links = await LoadLinksAsync(ct);
        var scope = await EnsureLotReadAsync(links, lot.Id, ct);
        var placed = await LoadPlacedCountsAsync(scope, ct);
        return ApiResponse<CrabLotDto>.Ok(MapLot(lot, placed, links, scope));
    }

    public async Task<ApiResponse<NextCrabLotCodeDto>> GetNextLotCodeAsync(
        DateTime? importDate = null, CancellationToken ct = default)
    {
        var scope = await _areas.AreaIdsAsync(ct);
        if (scope is { Count: 0 })
            throw AppException.Forbidden("Bạn chưa được phân công khu nuôi.");
        var code = await PeekNextLotCodeAsync(importDate ?? DateTime.UtcNow, ct);
        return ApiResponse<NextCrabLotCodeDto>.Ok(new NextCrabLotCodeDto(code));
    }

    public async Task<ApiResponse<CrabLotDto>> CreateLotAsync(CreateCrabLotRequest req, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw AppException.BadRequest("Name is required — tên lô cua.");
        if (req.Quantity <= 0)
            throw AppException.BadRequest("Quantity is required and must be > 0.");
        if (req.DeadOnArrival < 0 || req.DeadOnArrival > req.Quantity)
            throw AppException.BadRequest("DeadOnArrival must be between 0 and Quantity.");

        var importDate = req.ImportDate is { } d && d != default ? d : DateTime.UtcNow;
        var areaIds = await RequireCreateAreasAsync(req, ct);
        var code = string.IsNullOrWhiteSpace(req.LotCode)
            ? await AllocateLotCodeAsync(importDate, ct)
            : req.LotCode.Trim();
        if (await _uow.CrabLots.AnyAsync(l => l.LotCode == code, ct))
            throw AppException.Conflict($"LotCode '{code}' already exists.");

        var lot = new CrabLot
        {
            LotCode = code,
            Name = name,
            ImportDate = importDate,
            Quantity = req.Quantity,
            SupplierName = TrimOrNull(req.SupplierName),
            TotalWeightKg = PositiveOrNull(req.TotalWeightKg),
            WeightMinGram = PositiveOrNull(req.WeightMinGram),
            WeightMaxGram = PositiveOrNull(req.WeightMaxGram),
            UnitPriceVndPerKg = NonNegOrNull(req.UnitPriceVndPerKg),
            ShippingCostVnd = NonNegOrNull(req.ShippingCostVnd),
            OtherCostVnd = NonNegOrNull(req.OtherCostVnd),
            Condition = ParseCondition(req.Condition),
            Status = "Pending",
            DeadOnArrival = req.DeadOnArrival,
            Notes = TrimOrNull(req.Notes),
            StockedAt = req.StockedAt is { } stocked && stocked != default ? stocked : importDate
        };
        RecalcDerived(lot);
        await _uow.CrabLots.AddAsync(lot, ct);
        await AddAreaLinksAsync(lot.Id, areaIds, ct);
        await ConfigAudit.WriteAsync(
            _uow, (await _areas.CurrentUserAsync(ct))?.Id,
            "CreateCrabLot", "CrabLot", lot.Id, lot.LotCode, ct);
        await _uow.SaveChangesAsync(ct);

        var links = await LoadLinksAsync(ct);
        var placed = await LoadPlacedCountsAsync(ct);
        return ApiResponse<CrabLotDto>.Ok(MapLot(lot, placed, links),
            $"Imported {lot.LotCode}. Next: place crabs into boxes.");
    }

    public async Task<ApiResponse<CrabLotDto>> UpdateLotAsync(Guid id, UpdateCrabLotRequest req, CancellationToken ct = default)
    {
        var lot = await _uow.CrabLots.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("CrabLot");
        var links = await LoadLinksAsync(ct);
        await EnsureLotManageAsync(links, lot.Id, ct);
        if (lot.ClosedAt is not null)
            throw AppException.Conflict("Lô đã kết thúc, không sửa thông tin.");

        IReadOnlyList<Guid>? replacementAreas = null;
        if (req.FarmingAreaIds is not null)
            replacementAreas = await ValidateAreaLinkReplacementAsync(
                lot.Id, req.FarmingAreaIds, links, ct);

        if (req.Name is not null)
        {
            var name = req.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw AppException.BadRequest("Name cannot be empty.");
            lot.Name = name;
        }
        if (req.ImportDate is { } d && d != default) lot.ImportDate = d;
        if (req.Quantity is int q)
        {
            if (q <= 0) throw AppException.BadRequest("Quantity must be > 0.");
            lot.Quantity = q;
        }
        if (req.SupplierName is not null) lot.SupplierName = TrimOrNull(req.SupplierName);
        if (req.TotalWeightKg is not null) lot.TotalWeightKg = PositiveOrNull(req.TotalWeightKg);
        if (req.WeightMinGram is not null) lot.WeightMinGram = PositiveOrNull(req.WeightMinGram);
        if (req.WeightMaxGram is not null) lot.WeightMaxGram = PositiveOrNull(req.WeightMaxGram);
        if (req.UnitPriceVndPerKg is not null) lot.UnitPriceVndPerKg = NonNegOrNull(req.UnitPriceVndPerKg);
        if (req.ShippingCostVnd is not null) lot.ShippingCostVnd = NonNegOrNull(req.ShippingCostVnd);
        if (req.OtherCostVnd is not null) lot.OtherCostVnd = NonNegOrNull(req.OtherCostVnd);
        if (req.Condition is not null) lot.Condition = ParseCondition(req.Condition);
        if (req.DeadOnArrival is int dead)
        {
            if (dead < 0 || dead > lot.Quantity)
                throw AppException.BadRequest("DeadOnArrival must be between 0 and Quantity.");
            lot.DeadOnArrival = dead;
        }
        if (req.Notes is not null) lot.Notes = TrimOrNull(req.Notes);
        if (req.StockedAt is { } stocked && stocked != default) lot.StockedAt = stocked;
        if (req.Status is not null)
        {
            var next = ParseWorkflowStatus(req.Status, allowCancel: true);
            if (next is "Completed" or "Allocating")
                throw AppException.BadRequest("Trạng thái phân bổ không dùng để kết thúc lô. Gọi POST /api/crab-lots/{id}/close.");
            lot.Status = next;
        }
        if (replacementAreas is not null)
            await ReplaceAreaLinksAsync(lot.Id, replacementAreas, ct);
        RecalcDerived(lot);
        _uow.CrabLots.Update(lot);
        await ConfigAudit.WriteAsync(
            _uow, (await _areas.CurrentUserAsync(ct))?.Id,
            "UpdateCrabLot", "CrabLot", lot.Id, lot.LotCode, ct);
        await _uow.SaveChangesAsync(ct);

        links = await LoadLinksAsync(ct);
        var placed = await LoadPlacedCountsAsync(ct);
        return ApiResponse<CrabLotDto>.Ok(MapLot(lot, placed, links));
    }

    public async Task<ApiResponse> DeleteLotAsync(Guid id, CancellationToken ct = default)
    {
        var lot = await _uow.CrabLots.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("CrabLot");
        await EnsureLotManageAsync(await LoadLinksAsync(ct), lot.Id, ct);
        if (lot.ClosedAt is not null)
            throw AppException.Conflict(
                "Lô đã kết thúc phải được giữ lại để bảo toàn lịch sử và snapshot báo cáo.");

        var inUse = await _uow.Crabs.AnyAsync(c => c.CrabLotId == id, ct);
        if (inUse)
            throw AppException.Conflict(
                $"Cannot delete crab lot '{lot.LotCode}' — crabs still reference it.");

        _uow.CrabLots.Remove(lot);
        await ConfigAudit.WriteAsync(
            _uow, (await _areas.CurrentUserAsync(ct))?.Id,
            "DeleteCrabLot", "CrabLot", lot.Id, lot.LotCode, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok($"Crab lot '{lot.LotCode}' deleted.");
    }

    public async Task<ApiResponse<CrabLotDto>> CloseLotAsync(
        Guid id, CloseCrabLotRequest req, CancellationToken ct = default)
    {
        var lot = await _uow.CrabLots.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("CrabLot");
        var links = await LoadLinksAsync(ct);
        await EnsureLotManageAsync(links, lot.Id, ct);
        if (lot.ClosedAt is not null)
            throw AppException.Conflict("Lô đã kết thúc.");
        if (IsCancelled(lot.Status))
            throw AppException.Conflict("Lô đã hủy, không kết thúc.");

        if (lot.StockedAt is null)
            throw AppException.BadRequest(
                "Lô chưa có ngày bắt đầu thả cua, không thể xác nhận ngày kết thúc.");
        var closedAt = req.ClosedAt is { } requested && requested != default
            ? requested
            : DateTime.UtcNow;
        if (closedAt < lot.StockedAt.Value)
            throw AppException.BadRequest("Ngày kết thúc không được trước ngày bắt đầu thả cua.");

        var crabs = (await _uow.Crabs.FindAsync(crab => crab.CrabLotId == id, ct)).ToList();
        var crabIds = crabs.Select(crab => crab.Id).ToHashSet();
        var successfullyMoltedIds = (await _uow.MoltingRecords.GetAllAsync(ct))
            .Where(record => crabIds.Contains(record.CrabId)
                && !record.IsVoided
                && string.Equals(record.Result, "success", StringComparison.OrdinalIgnoreCase))
            .Select(record => record.CrabId)
            .ToHashSet();
        successfullyMoltedIds.UnionWith(
            crabs.Where(crab => crab.MoltedAt is not null).Select(crab => crab.Id));

        var registered = crabs.Count;
        var alive = crabs.Count(crab => IsLiving(crab.Status));
        var unmolted = crabs.Count(
            crab => IsLiving(crab.Status) && !successfullyMoltedIds.Contains(crab.Id));
        var molted = successfullyMoltedIds.Count;
        var harvested = crabs.Count(
            crab => crab.Status is CrabStatus.Harvested or CrabStatus.Sold);
        var dead = crabs.Count(crab => crab.Status == CrabStatus.Dead);
        var unstocked = Math.Max(0, lot.Quantity - lot.DeadOnArrival - registered);
        var disposition = TrimOrNull(req.Disposition);
        if (unmolted > 0 && disposition is null)
            throw AppException.BadRequest("Lô còn cua chưa lột. Cần ghi phương án xử lý khi kết thúc.");

        lot.ClosedAt = closedAt;
        lot.RegisteredCountAtClose = registered;
        lot.AliveCountAtClose = alive;
        lot.UnmoltedCountAtClose = unmolted;
        lot.MoltedCountAtClose = molted;
        lot.HarvestedCountAtClose = harvested;
        lot.DeadCountAtClose = dead;
        lot.UnstockedCountAtClose = unstocked;
        lot.CloseDisposition = disposition;
        _uow.CrabLots.Update(lot);
        await ConfigAudit.WriteAsync(
            _uow, (await _areas.CurrentUserAsync(ct))?.Id,
            "CloseCrabLot", "CrabLot", lot.Id,
            $"{lot.LotCode}|registered={registered}|alive={alive}|unmolted={unmolted}"
            + $"|molted={molted}|harvested={harvested}|dead={dead}|unstocked={unstocked}",
            ct);
        await _uow.SaveChangesAsync(ct);

        var placed = await LoadPlacedCountsAsync(ct);
        return ApiResponse<CrabLotDto>.Ok(MapLot(lot, placed, links), "Lot closed.");
    }

    public async Task<ApiResponse<IReadOnlyList<CrabImageDto>>> UploadImagesAsync(
        Guid lotId,
        IReadOnlyList<CrabImageFile> files,
        Guid? uploadedBy,
        CancellationToken ct = default)
    {
        ImageUploadRules.ValidateBatch(files);
        var lot = await _uow.CrabLots.GetByIdAsync(lotId, ct)
            ?? throw AppException.NotFound("CrabLot");
        await EnsureLotManageAsync(await LoadLinksAsync(ct), lot.Id, ct);
        if (lot.ClosedAt.HasValue)
            throw AppException.Conflict("Không thể thêm ảnh cho Crab Lot đã kết thúc.");

        var existing = JsonStringList.Parse(lot.ImageUrlsJson);
        if (existing.Count + files.Count > ImageUploadRules.MaxUrls)
            throw AppException.BadRequest($"A crab lot can have at most {ImageUploadRules.MaxUrls} images.");

        var folder = MediaFolderPath.Join(MediaFolderPath.InboundRoot, lot.LotCode);
        var results = new List<CrabImageDto>(files.Count);
        var urls = new List<string>(files.Count);

        foreach (var file in files)
        {
            var uploaded = await _images.UploadAsync(
                file.Data, file.FileName, file.ContentType, folder, ct);
            var url = MediaPhotoResolver.PublicUrl(uploaded);

            await _uow.MediaAssets.AddAsync(new MediaAsset
            {
                Category = "image",
                FileName = file.FileName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType,
                SizeBytes = uploaded.SizeBytes,
                Provider = _images.ProviderName,
                StorageKey = uploaded.StorageKey,
                WebViewLink = uploaded.WebViewLink,
                WebContentLink = uploaded.WebContentLink,
                ShareLink = url,
                IsShared = true,
                RelatedEntityType = "CrabLot",
                RelatedEntityId = lot.Id,
                Notes = "crab-lot-image",
                UploadedBy = uploadedBy
            }, ct);

            results.Add(new CrabImageDto(url, uploaded.StorageKey, file.FileName, _images.ProviderName, uploaded.SizeBytes));
            urls.Add(url);
        }

        lot.ImageUrlsJson = JsonStringList.Serialize(
            JsonStringList.Merge(lot.ImageUrlsJson, urls, ImageUploadRules.MaxUrls), ImageUploadRules.MaxUrls);
        _uow.CrabLots.Update(lot);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<IReadOnlyList<CrabImageDto>>.Ok(results, "Uploaded.");
    }

    public async Task<CrabImageContent?> GetPhotoAsync(Guid lotId, int index, CancellationToken ct = default)
    {
        if (index < 0) return null;
        var lot = await _uow.CrabLots.GetByIdAsync(lotId, ct);
        if (lot is null) return null;
        var links = await LoadLinksAsync(ct);
        var scope = await EnsureLotReadAsync(links, lot.Id, ct);
        if (scope is not null
            && links.TryGetValue(lot.Id, out var lotAreas)
            && lotAreas.Any(areaId => !scope.Contains(areaId)))
        {
            throw AppException.Forbidden(
                "Không thể xem ảnh toàn lô khi bạn không được phân công tất cả khu của lô.");
        }

        var urls = JsonStringList.Parse(lot.ImageUrlsJson).ToList();
        var assets = (await _uow.MediaAssets.FindAsync(
                m => m.RelatedEntityId == lotId && m.RelatedEntityType == "CrabLot",
                ct))
            .OrderBy(m => m.CreatedAt)
            .ToList();

        foreach (var asset in assets)
        {
            var link = asset.ShareLink ?? asset.WebContentLink ?? asset.WebViewLink;
            if (!string.IsNullOrWhiteSpace(link) && !urls.Contains(link, StringComparer.OrdinalIgnoreCase))
                urls.Add(link);
        }

        if (index >= urls.Count) return null;
        var url = urls[index];
        var assetMatch = assets.FirstOrDefault(a =>
            string.Equals(a.ShareLink, url, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.WebContentLink, url, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.WebViewLink, url, StringComparison.OrdinalIgnoreCase));

        return await MediaPhotoResolver.OpenAsync(_images, url, assetMatch, ct);
    }

    private async Task<Dictionary<Guid, int>> LoadPlacedCountsAsync(CancellationToken ct) =>
        await LoadPlacedCountsAsync(null, ct);

    private async Task<Dictionary<Guid, int>> LoadPlacedCountsAsync(
        HashSet<Guid>? scope,
        CancellationToken ct)
    {
        var crabs = await _uow.Crabs.GetAllAsync(ct);
        if (scope is not null)
        {
            var rows = (await _uow.FarmingRows.GetAllAsync(ct))
                .Where(row => scope.Contains(row.FarmingAreaId))
                .Select(row => row.Id)
                .ToHashSet();
            var boxIds = (await _uow.Boxes.GetAllAsync(ct))
                .Where(box => rows.Contains(box.FarmingRowId))
                .Select(box => box.Id)
                .ToHashSet();
            crabs = crabs.Where(crab => crab.BoxId is Guid boxId && boxIds.Contains(boxId));
        }
        return crabs.GroupBy(c => c.CrabLotId).ToDictionary(g => g.Key, g => g.Count());
    }

    private static CrabLotDto MapLot(
        CrabLot l,
        IReadOnlyDictionary<Guid, int> placed,
        IReadOnlyDictionary<Guid, List<Guid>>? links = null,
        HashSet<Guid>? scope = null)
    {
        placed.TryGetValue(l.Id, out var count);
        IReadOnlyList<Guid> allAreas = links is not null && links.TryGetValue(l.Id, out var found)
            ? found
            : Array.Empty<Guid>();
        var partial = scope is not null && allAreas.Any(areaId => !scope.Contains(areaId));
        IReadOnlyList<Guid> areas = scope is null
            ? allAreas
            : allAreas.Where(scope.Contains).ToArray();
        var displayedCount = !partial && l.ClosedAt is not null
            ? l.RegisteredCountAtClose ?? count
            : count;
        return new(
            l.Id,
            l.LotCode,
            string.IsNullOrWhiteSpace(l.Name) ? l.LotCode : l.Name,
            l.ImportDate,
            partial ? displayedCount : l.Quantity,
            displayedCount,
            partial ? null : l.SupplierName,
            partial ? null : l.TotalWeightKg,
            partial ? null : l.AverageWeightGram,
            partial ? null : l.WeightMinGram,
            partial ? null : l.WeightMaxGram,
            partial ? null : l.UnitPriceVndPerKg,
            partial ? null : l.CrabCostVnd,
            partial ? null : l.ShippingCostVnd,
            partial ? null : l.OtherCostVnd,
            partial ? null : l.TotalCostVnd,
            string.IsNullOrWhiteSpace(l.Condition) ? "Good" : l.Condition,
            partial ? 0 : l.DeadOnArrival,
            partial ? null : l.Notes,
            partial
                ? IsCancelled(l.Status) ? "Cancelled" : count > 0 ? "Allocating" : "Pending"
                : ResolveWorkflowStatus(l.Status, displayedCount, l.Quantity),
            partial ? Array.Empty<string>() : JsonStringList.Parse(l.ImageUrlsJson),
            l.StockedAt,
            l.ClosedAt,
            partial ? null : l.UnmoltedCountAtClose,
            partial ? null : l.CloseDisposition,
            areas,
            partial,
            partial ? null : l.RegisteredCountAtClose,
            partial ? null : l.AliveCountAtClose,
            partial ? null : l.MoltedCountAtClose,
            partial ? null : l.HarvestedCountAtClose,
            partial ? null : l.DeadCountAtClose,
            partial ? null : l.UnstockedCountAtClose);
    }

    private async Task<List<Guid>> RequireCreateAreasAsync(CreateCrabLotRequest req, CancellationToken ct)
    {
        var requested = NormalizeAreaIds(req.FarmingAreaIds, req.FarmingAreaId);
        var user = await _areas.CurrentUserAsync(ct);
        if (user is null)
            return requested;
        if (requested.Count == 0)
            throw AppException.BadRequest("Lô cua phải gắn với ít nhất một khu nuôi.");
        await _areas.EnsureAllAssignedAsync(requested, ct);
        return requested;
    }

    private async Task ReplaceAreaLinksAsync(Guid lotId, IReadOnlyList<Guid>? areaIds, CancellationToken ct)
    {
        var requested = NormalizeAreaIds(areaIds, null);
        if (_uow.CrabLotAreas is null) return;
        var current = (await _uow.CrabLotAreas.GetAllAsync(ct)).Where(link => link.CrabLotId == lotId).ToList();
        foreach (var link in current.Where(link => !requested.Contains(link.FarmingAreaId)))
            _uow.CrabLotAreas.Remove(link);
        var existing = current.Select(link => link.FarmingAreaId).ToHashSet();
        await AddAreaLinksAsync(lotId, requested.Where(id => !existing.Contains(id)), ct);
    }

    private async Task<IReadOnlyList<Guid>> ValidateAreaLinkReplacementAsync(
        Guid lotId,
        IReadOnlyList<Guid> areaIds,
        IReadOnlyDictionary<Guid, List<Guid>> links,
        CancellationToken ct)
    {
        var requested = NormalizeAreaIds(areaIds, null);
        if (requested.Count == 0)
            throw AppException.BadRequest("Lô cua phải gắn với ít nhất một khu nuôi.");

        await _areas.EnsureAllAssignedAsync(requested, ct);
        var current = links.TryGetValue(lotId, out var linked)
            ? linked
            : new List<Guid>();
        var removed = current.Where(areaId => !requested.Contains(areaId)).ToHashSet();
        if (removed.Count == 0)
            return requested;

        var removedRowIds = (await _uow.FarmingRows.GetAllAsync(ct))
            .Where(row => removed.Contains(row.FarmingAreaId))
            .Select(row => row.Id)
            .ToHashSet();
        var removedBoxIds = (await _uow.Boxes.GetAllAsync(ct))
            .Where(box => removedRowIds.Contains(box.FarmingRowId))
            .Select(box => box.Id)
            .ToHashSet();
        var lotCrabs = (await _uow.Crabs.FindAsync(crab => crab.CrabLotId == lotId, ct)).ToList();
        var crabIds = lotCrabs.Select(crab => crab.Id).ToHashSet();

        var hasCurrentCrab = lotCrabs.Any(
            crab => crab.BoxId is Guid boxId && removedBoxIds.Contains(boxId));
        var hasAllocationHistory = (await _uow.CrabBoxAllocations.GetAllAsync(ct)).Any(
            item => crabIds.Contains(item.CrabId) && removedBoxIds.Contains(item.BoxId));
        var hasMoltingHistory = (await _uow.MoltingRecords.GetAllAsync(ct)).Any(
            item => crabIds.Contains(item.CrabId)
                && item.BoxId is Guid boxId
                && removedBoxIds.Contains(boxId));
        var hasMortalityHistory = (await _uow.CrabMortalityRecords.GetAllAsync(ct)).Any(
            item => crabIds.Contains(item.CrabId)
                && item.BoxId is Guid boxId
                && removedBoxIds.Contains(boxId));
        var hasHarvestHistory = (await _uow.HarvestLines.GetAllAsync(ct)).Any(
            item => item.CrabId is Guid crabId
                && crabIds.Contains(crabId)
                && item.BoxId is Guid boxId
                && removedBoxIds.Contains(boxId));

        if (hasCurrentCrab
            || hasAllocationHistory
            || hasMoltingHistory
            || hasMortalityHistory
            || hasHarvestHistory)
        {
            throw AppException.Conflict(
                "Không thể bỏ khu khỏi lô vì khu còn cua hoặc lịch sử nghiệp vụ của lô.");
        }

        return requested;
    }

    private async Task AddAreaLinksAsync(Guid lotId, IEnumerable<Guid> areaIds, CancellationToken ct)
    {
        if (_uow.CrabLotAreas is null) return;
        foreach (var areaId in areaIds.Distinct())
        {
            await _uow.CrabLotAreas.AddAsync(new CrabLotArea
            {
                CrabLotId = lotId,
                FarmingAreaId = areaId
            }, ct);
        }
    }

    private async Task<HashSet<Guid>?> EnsureLotReadAsync(
        IReadOnlyDictionary<Guid, List<Guid>> links,
        Guid lotId,
        CancellationToken ct)
    {
        var scope = await _areas.AreaIdsAsync(ct);
        if (scope is null) return null;
        if (!LinksOverlap(links, lotId, scope))
            throw AppException.Forbidden("Bạn không được phân công khu của lô này.");
        return scope;
    }

    private async Task EnsureLotManageAsync(
        IReadOnlyDictionary<Guid, List<Guid>> links,
        Guid lotId,
        CancellationToken ct)
    {
        var scope = await _areas.AreaIdsAsync(ct);
        if (scope is null) return;
        if (!links.TryGetValue(lotId, out var lotAreas) || lotAreas.Count == 0)
            throw AppException.Forbidden("Lô cua chưa có khu nuôi hợp lệ để quản lý.");
        if (lotAreas.Any(areaId => !scope.Contains(areaId)))
            throw AppException.Forbidden(
                "Bạn phải được phân công tất cả khu của Crab Lot để quản lý toàn lô.");
    }

    private static bool LinksOverlap(
        IReadOnlyDictionary<Guid, List<Guid>> links, Guid lotId, HashSet<Guid> scope) =>
        links.TryGetValue(lotId, out var areas) && areas.Any(scope.Contains);

    private async Task<Dictionary<Guid, List<Guid>>> LoadLinksAsync(CancellationToken ct)
    {
        if (_uow.CrabLotAreas is null)
            return new Dictionary<Guid, List<Guid>>();
        var rows = await _uow.CrabLotAreas.GetAllAsync(ct);
        return rows
            .GroupBy(link => link.CrabLotId)
            .ToDictionary(group => group.Key, group => group.Select(link => link.FarmingAreaId).Distinct().ToList());
    }

    private static List<Guid> NormalizeAreaIds(IReadOnlyList<Guid>? many, Guid? one)
    {
        var ids = new List<Guid>();
        if (many is not null)
            ids.AddRange(many.Where(id => id != Guid.Empty));
        if (one is Guid single && single != Guid.Empty && !ids.Contains(single))
            ids.Add(single);
        return ids.Distinct().ToList();
    }

    private static void RecalcDerived(CrabLot lot)
    {
        if (lot.TotalWeightKg is > 0 && lot.Quantity > 0)
            lot.AverageWeightGram = Math.Round(lot.TotalWeightKg.Value * 1000m / lot.Quantity, 2);

        lot.CrabCostVnd = lot.UnitPriceVndPerKg is > 0 && lot.TotalWeightKg is > 0
            ? Math.Round(lot.UnitPriceVndPerKg.Value * lot.TotalWeightKg.Value, 0)
            : null;
        lot.TotalCostVnd = (lot.CrabCostVnd ?? 0)
            + (lot.ShippingCostVnd ?? 0)
            + (lot.OtherCostVnd ?? 0);
        if (lot.TotalCostVnd == 0 && lot.CrabCostVnd is null)
            lot.TotalCostVnd = null;
    }

    private async Task<string> AllocateLotCodeAsync(DateTime importDate, CancellationToken ct)
    {
        for (var i = 0; i < 8; i++)
        {
            var code = await PeekNextLotCodeAsync(importDate, ct);
            if (!await _uow.CrabLots.AnyAsync(l => l.LotCode == code, ct))
                return code;
        }
        throw AppException.Conflict("Could not allocate a unique lot code. Retry.");
    }

    private async Task<string> PeekNextLotCodeAsync(DateTime importDate, CancellationToken ct)
    {
        var day = importDate.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(importDate.Date, DateTimeKind.Utc)
            : importDate.ToUniversalTime().Date;
        var prefix = $"LOT-{day:yyyyMMdd}-";
        var lots = await _uow.CrabLots.GetAllAsync(ct);
        var max = 0;
        foreach (var lot in lots)
        {
            if (!lot.LotCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = lot.LotCode[prefix.Length..];
            if (int.TryParse(suffix, out var n) && n > max) max = n;
        }
        return $"{prefix}{(max + 1):D3}";
    }

    private static string ResolveWorkflowStatus(string? stored, int placed, int quantity)
    {
        if (IsCancelled(stored)) return "Cancelled";
        if (placed <= 0) return "Pending";
        if (placed < quantity) return "Allocating";
        return "Completed";
    }

    private static bool IsCancelled(string? raw)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
        return key is "cancelled" or "canceled" or "dahuy" or "đãhủy";
    }

    private static bool IsLiving(CrabStatus status) =>
        status is CrabStatus.Alive or CrabStatus.Molting or CrabStatus.Quarantined;

    private static string ParseWorkflowStatus(string raw, bool allowCancel)
    {
        var key = raw.Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
        return key switch
        {
            "cancelled" or "canceled" or "dahuy" or "đãhủy" when allowCancel => "Cancelled",
            "pending" or "choxuly" or "chờxửlý" => "Pending",
            "allocating" or "inprogress" or "dangphanhop" or "đangphânhộp" => "Allocating",
            "completed" or "done" or "hoantat" or "đãhoàntất" => "Completed",
            _ => allowCancel && IsCancelled(raw) ? "Cancelled" : "Pending"
        };
    }

    private static string ParseCondition(string? raw)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant()
            .Replace(" ", "", StringComparison.Ordinal);
        return key switch
        {
            "average" or "medium" or "trungbinh" or "trungbình" => "Average",
            "problem" or "bad" or "covande" or "cóvấnđề" => "Problem",
            _ => "Good"
        };
    }

    private static string? TrimOrNull(string? v)
        => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static decimal? PositiveOrNull(decimal? v)
        => v is > 0 ? v : null;

    private static decimal? NonNegOrNull(decimal? v)
        => v is >= 0 ? v : null;
}
