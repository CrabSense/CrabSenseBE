using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public sealed class CrabLotComparisonReportService
    : ICrabLotComparisonReportService
{
    private readonly IUnitOfWork _uow;
    private readonly ReportAreaGate _areas;

    public CrabLotComparisonReportService(
        IUnitOfWork uow,
        ICurrentUserService currentUser)
    {
        _uow = uow;
        _areas = new ReportAreaGate(uow, currentUser);
    }

    public async Task<IReadOnlyList<CrabLotCompareRowDto>> CompareAsync(
        Guid? farmingAreaId,
        CancellationToken ct = default)
    {
        var selectedAreas = await _areas.AreasAsync(farmingAreaId, ct);
        if (selectedAreas is { Count: 0 })
            return Array.Empty<CrabLotCompareRowDto>();

        var visibleLinks = selectedAreas is null
            ? await _uow.CrabLotAreas.GetAllAsync(ct)
            : await _uow.CrabLotAreas.FindAsync(
                link => selectedAreas.Contains(link.FarmingAreaId), ct);
        var visibleLotIds = visibleLinks
            .Select(link => link.CrabLotId)
            .ToHashSet();
        if (visibleLotIds.Count == 0)
            return Array.Empty<CrabLotCompareRowDto>();

        var lotAreaLinks = (await _uow.CrabLotAreas.FindAsync(
                link => visibleLotIds.Contains(link.CrabLotId), ct))
            .GroupBy(link => link.CrabLotId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(link => link.FarmingAreaId)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToHashSet());
        var lots = (await _uow.CrabLots.FindAsync(
                lot => visibleLotIds.Contains(lot.Id), ct))
            .Where(lot => visibleLotIds.Contains(lot.Id))
            .ToList();
        var crabs = (await _uow.Crabs.FindAsync(
                crab => visibleLotIds.Contains(crab.CrabLotId), ct))
            .Where(crab => visibleLotIds.Contains(crab.CrabLotId))
            .DistinctBy(crab => crab.Id)
            .ToList();
        var crabIds = crabs.Select(crab => crab.Id).ToHashSet();

        var rowsById = (await _uow.FarmingRows.GetAllAsync(ct))
            .ToDictionary(row => row.Id);
        var areaByBoxId = (await _uow.Boxes.GetAllAsync(ct))
            .Where(box => rowsById.ContainsKey(box.FarmingRowId))
            .ToDictionary(
                box => box.Id,
                box => rowsById[box.FarmingRowId].FarmingAreaId);
        var allocationsByCrab = (await _uow.CrabBoxAllocations.FindAsync(
                item => crabIds.Contains(item.CrabId), ct))
            .Where(item => crabIds.Contains(item.CrabId))
            .GroupBy(item => item.CrabId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var completedVouchers = (await _uow.HarvestVouchers.GetAllAsync(ct))
            .Where(voucher => voucher.Status == HarvestStatus.Completed)
            .ToDictionary(voucher => voucher.Id);
        var completedLines = (await _uow.HarvestLines.FindAsync(
                line => line.CrabId.HasValue
                    && crabIds.Contains(line.CrabId.Value), ct))
            .Where(line => line.CrabId is Guid crabId
                && crabIds.Contains(crabId)
                && completedVouchers.ContainsKey(line.HarvestVoucherId)
                && !string.Equals(
                    line.Result, "failed", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var linesById = completedLines.ToDictionary(line => line.Id);
        var approvedLines = (await _uow.HarvestApprovedLines.FindAsync(
                line => line.CrabId.HasValue
                    && crabIds.Contains(line.CrabId.Value), ct))
            .Where(line => line.CrabId is Guid crabId
                && crabIds.Contains(crabId)
                && completedVouchers.ContainsKey(line.HarvestVoucherId))
            .ToList();
        var historiesByCrab = (await _uow.CrabHarvestHistories.FindAsync(
                history => crabIds.Contains(history.CrabId), ct))
            .Where(history => crabIds.Contains(history.CrabId))
            .GroupBy(history => history.CrabId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var successfulMoltIds = (await _uow.MoltingRecords.FindAsync(
                record => crabIds.Contains(record.CrabId), ct))
            .Where(record => crabIds.Contains(record.CrabId)
                && !record.IsVoided
                && string.Equals(
                    record.Result, "success", StringComparison.OrdinalIgnoreCase))
            .Select(record => record.CrabId)
            .ToHashSet();
        successfulMoltIds.UnionWith(
            crabs.Where(crab => crab.MoltedAt.HasValue)
                .Select(crab => crab.Id));

        HarvestEvidence? HarvestOf(Crab crab)
        {
            var historyCandidates = historiesByCrab
                .GetValueOrDefault(crab.Id, new List<CrabHarvestHistory>())
                .Where(history => history.HarvestLineId is Guid lineId
                    && linesById.ContainsKey(lineId))
                .OrderByDescending(history => history.HarvestedAt)
                .ToList();
            if (historyCandidates.Count > 0)
            {
                var history = historyCandidates[0];
                var line = linesById[history.HarvestLineId!.Value];
                return EvidenceOf(
                    crab.Id,
                    line,
                    history.WeightGram ?? line.WeightGram,
                    history.HarvestedAt);
            }

            var fallbackLine = completedLines
                .Where(item => item.CrabId == crab.Id)
                .OrderByDescending(item =>
                    completedVouchers[item.HarvestVoucherId].ConfirmedAt
                    ?? completedVouchers[item.HarvestVoucherId].HarvestDate)
                .ThenByDescending(item => item.CreatedAt)
                .FirstOrDefault();
            return fallbackLine is null
                ? null
                : EvidenceOf(
                    crab.Id,
                    fallbackLine,
                    fallbackLine.WeightGram,
                    completedVouchers[fallbackLine.HarvestVoucherId].ConfirmedAt
                    ?? completedVouchers[fallbackLine.HarvestVoucherId].HarvestDate);
        }

        HarvestEvidence EvidenceOf(
            Guid crabId,
            HarvestLine line,
            decimal weightGram,
            DateTime at)
        {
            var voucher = completedVouchers[line.HarvestVoucherId];
            var candidateAreas = approvedLines
                .Where(snapshot =>
                    snapshot.HarvestVoucherId == voucher.Id
                    && snapshot.ApprovalVersion == voucher.ApprovalVersion
                    && (snapshot.CrabId == crabId
                        || snapshot.SourceHarvestLineId == line.Id))
                .Select(snapshot => snapshot.FarmingAreaId)
                .Where(id => id != Guid.Empty)
                .ToHashSet();
            if (voucher.FarmingAreaId is Guid voucherAreaId
                && voucherAreaId != Guid.Empty)
                candidateAreas.Add(voucherAreaId);
            if (line.BoxId is Guid lineBoxId
                && areaByBoxId.TryGetValue(lineBoxId, out var lineAreaId))
                candidateAreas.Add(lineAreaId);

            return new HarvestEvidence(
                candidateAreas.Count == 1 ? candidateAreas.Single() : null,
                weightGram,
                at);
        }

        Guid? OperationalAreaOf(Crab crab)
        {
            var validAllocations = allocationsByCrab
                .GetValueOrDefault(crab.Id, new List<CrabBoxAllocation>())
                .Where(item => areaByBoxId.ContainsKey(item.BoxId))
                .ToList();
            if (validAllocations.Count > 0)
            {
                var latestStart = validAllocations.Max(item => item.StartTime);
                var latestAreas = validAllocations
                    .Where(item => item.StartTime == latestStart)
                    .Select(item => areaByBoxId[item.BoxId])
                    .Distinct()
                    .ToArray();
                return latestAreas.Length == 1 ? latestAreas[0] : null;
            }

            return crab.BoxId is Guid currentBoxId
                && areaByBoxId.TryGetValue(currentBoxId, out var currentAreaId)
                    ? currentAreaId
                    : null;
        }

        var resolved = crabs.ToDictionary(
            crab => crab.Id,
            crab =>
            {
                var harvest = HarvestOf(crab);
                var areaId = harvest is not null
                    || crab.Status is CrabStatus.Harvested or CrabStatus.Sold
                        ? harvest?.AreaId
                        : OperationalAreaOf(crab);
                if (areaId is Guid resolvedArea
                    && (!lotAreaLinks.TryGetValue(crab.CrabLotId, out var linkedAreas)
                        || !linkedAreas.Contains(resolvedArea)))
                    areaId = null;
                return new ResolvedCrab(crab, areaId, harvest);
            });

        var result = new List<CrabLotCompareRowDto>(lots.Count);
        foreach (var lot in lots.OrderBy(item => item.LotCode))
        {
            var linkedAreas = lotAreaLinks.GetValueOrDefault(
                lot.Id, new HashSet<Guid>());
            var visibleAreas = selectedAreas is null
                ? linkedAreas
                : linkedAreas.Where(selectedAreas.Contains).ToHashSet();
            var partial = selectedAreas is not null
                && !linkedAreas.IsSubsetOf(selectedAreas);
            var lotCrabs = crabs
                .Where(crab => crab.CrabLotId == lot.Id)
                .Select(crab => resolved[crab.Id])
                .ToList();
            var scopedCrabs = lotCrabs
                .Where(item => item.AreaId is Guid areaId
                    && visibleAreas.Contains(areaId))
                .ToList();
            var unresolvedCount = lotCrabs.Count(item => !item.AreaId.HasValue);
            var alive = scopedCrabs.Count(item =>
                item.Crab.Status is CrabStatus.Alive or CrabStatus.Molting);
            var molted = scopedCrabs.Count(item =>
                successfulMoltIds.Contains(item.Crab.Id));

            result.Add(new CrabLotCompareRowDto(
                lot.Id,
                lot.LotCode,
                lot.Name,
                lot.StockedAt,
                lot.ClosedAt,
                visibleAreas.OrderBy(id => id).ToArray(),
                partial,
                partial ? null : lot.Quantity,
                scopedCrabs.Count,
                alive,
                scopedCrabs.Count(item =>
                    (item.Crab.Status is CrabStatus.Alive or CrabStatus.Molting)
                    && !successfulMoltIds.Contains(item.Crab.Id)),
                molted,
                scopedCrabs.Count(item =>
                    item.Crab.Status is CrabStatus.Harvested or CrabStatus.Sold),
                scopedCrabs.Count(item => item.Harvest is not null),
                scopedCrabs.Sum(item => item.Harvest?.WeightGram ?? 0) / 1000m,
                partial ? null : unresolvedCount,
                partial ? null : lot.RegisteredCountAtClose,
                partial ? null : lot.AliveCountAtClose,
                partial ? null : lot.UnmoltedCountAtClose,
                partial ? null : lot.MoltedCountAtClose,
                partial ? null : lot.HarvestedCountAtClose,
                partial ? null : lot.DeadCountAtClose,
                partial ? null : lot.UnstockedCountAtClose));
        }

        return result;
    }

    private sealed record HarvestEvidence(
        Guid? AreaId,
        decimal WeightGram,
        DateTime At);

    private sealed record ResolvedCrab(
        Crab Crab,
        Guid? AreaId,
        HarvestEvidence? Harvest);
}
