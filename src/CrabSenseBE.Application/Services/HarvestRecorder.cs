using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Các quy tắc thu hoạch được HarvestService và
/// HarvestAssignmentService dùng chung.
/// </summary>
public sealed class HarvestRecorder
{
    private static readonly HashSet<string> AllowedGrades =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "S", "M", "L", "XL", "XXL", "A", "B", "C"
        };

    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;

    public HarvestRecorder(
        IUnitOfWork uow,
        ICurrentUserService currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
    }


    public void ValidateHarvestLines(
        IReadOnlyCollection<HarvestLineRequest> lines,
        bool requirePositiveWeight = true)
    {
        var duplicateCrabId = lines
            .Where(line => line.CrabId.HasValue)
            .GroupBy(line => line.CrabId!.Value)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateCrabId is not null)
        {
            throw AppException.BadRequest(
                $"Crab '{duplicateCrabId.Key}' appears more than once in the harvest request.");
        }

        var lineNumber = 0;

        foreach (var line in lines)
        {
            lineNumber++;

            if (requirePositiveWeight && line.WeightGram <= 0)
            {
                throw AppException.BadRequest(
                    $"WeightGram at line {lineNumber} must be greater than zero.");
            }

            if (!requirePositiveWeight && line.WeightGram < 0)
            {
                throw AppException.BadRequest(
                    $"WeightGram at line {lineNumber} cannot be negative.");
            }

            if (line.WeightGram > 10000)
            {
                throw AppException.BadRequest(
                    $"WeightGram at line {lineNumber} exceeds the allowed limit.");
            }

            if (!string.IsNullOrWhiteSpace(line.Grade)
                && !AllowedGrades.Contains(line.Grade.Trim()))
            {
                throw AppException.BadRequest(
                    $"Invalid grade at line {lineNumber}. Allowed values: S, M, L, XL, XXL, A, B, C.");
            }
        }
    }

    public async Task ValidateCrabsAsync(
        IReadOnlyCollection<Guid> crabIds,
        Guid farmingAreaId,
        bool checkAlreadyHarvested,
        Guid? currentVoucherId,
        CancellationToken ct,
        Guid? currentAssignmentId = null)
    {
        foreach (var crabId in crabIds)
        {
            var crab = await _uow.Crabs.GetByIdAsync(crabId, ct)
                ?? throw AppException.BadRequest(
                    $"Crab '{crabId}' does not exist.");

            if (crab.Status is not (CrabStatus.Alive or CrabStatus.Molting))
            {
                throw AppException.Conflict(
                    $"Crab '{crabId}' is not alive and cannot be harvested.");
            }

            var crabAreaId = await AreaOfCrabAsync(crab, ct);
            if (crabAreaId != farmingAreaId)
            {
                throw AppException.Forbidden(
                    $"Cua '{crabId}' không thuộc khu của phiếu thu hoạch.");
            }

            var alreadyHarvested = checkAlreadyHarvested
                && (await _uow.CrabHarvestHistories.AnyAsync(
                        history => history.CrabId == crabId,
                        ct)
                    || await HasActiveHarvestReservationAsync(
                        crabId,
                        currentVoucherId,
                        ct)
                    || await _uow.HarvestAssignmentLines.AnyAsync(
                        line => line.CrabId == crabId
                            && (!currentAssignmentId.HasValue || line.HarvestAssignmentId != currentAssignmentId.Value)
                            && (line.Status == HarvestAssignmentLineStatus.Assigned
                                || line.Status == HarvestAssignmentLineStatus.Eligible
                                || line.Status == HarvestAssignmentLineStatus.InProgress), ct));

            if (alreadyHarvested)
            {
                throw AppException.Conflict(
                    $"Crab '{crabId}' has already been recorded in another harvest voucher or open assignment.");
            }
        }
    }

    public async Task<Guid?> AreaOfCrabAsync(
        Crab crab,
        CancellationToken ct)
    {
        if (crab.BoxId is not Guid boxId)
            return null;

        var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
        if (box is null)
            return null;

        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
        return row?.FarmingAreaId;
    }

    public async Task<string> GenerateVoucherCodeAsync(
        CancellationToken ct)
    {
        var codes = (await _uow.HarvestVouchers.GetAllAsync(ct))
            .Select(voucher => voucher.VoucherCode)
            .Where(code => code.StartsWith(
                "HAR-",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        var max = 0;

        foreach (var code in codes)
        {
            var tail = code["HAR-".Length..];
            if (int.TryParse(tail, out var number) && number > max)
                max = number;
        }

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            var next = $"HAR-{(max + attempt):000}";

            if (!await _uow.HarvestVouchers.AnyAsync(
                    voucher => voucher.VoucherCode == next,
                    ct))
            {
                return next;
            }
        }

        throw AppException.Conflict(
            "Unable to generate a unique harvest voucher code.");
    }

    public async Task<HarvestLine> BuildLineAsync(
    Guid voucherId,
    HarvestLineRequest request,
    CancellationToken ct = default)
    {
        var snapshot = await SnapshotCrabAsync(request.CrabId, ct);

        return new HarvestLine
        {
            HarvestVoucherId = voucherId,
            CrabId = request.CrabId,
            BoxId = snapshot.BoxId,
            WeightGram = request.WeightGram,
            Grade = NormalizeGrade(request.Grade),
            IsSoftshell = request.IsSoftshell,
            Notes = NormalizeNullable(request.Notes),
            ConditionLabel = NormalizeNullable(request.ConditionLabel),
            PhotoUrlsJson = JsonStringList.Serialize(request.PhotoUrls),
            CrabCode = snapshot.CrabCode,
            AreaName = snapshot.AreaName,
            RowName = snapshot.RowName,
            BoxCode = snapshot.BoxCode,
            LotCode = snapshot.LotCode,
            Result = NormalizeResult(request.Result)
        };
    }

    public async Task ApplyHarvestAsync(
        HarvestVoucher voucher,
        IReadOnlyCollection<HarvestLine> lines,
        CancellationToken ct = default)
    {
        foreach (var line in lines.Where(item => item.CrabId.HasValue))
        {
            var crab = await _uow.Crabs.GetByIdAsync(line.CrabId!.Value, ct)
                ?? throw AppException.Conflict(
                    "Cua không còn tồn tại; cần đối soát phiếu.");

            if (crab.Status is not (CrabStatus.Alive or CrabStatus.Molting))
            {
                throw AppException.Conflict(
                    "Cua không còn đủ điều kiện thu hoạch; cần đối soát phiếu.");
            }

            var oldStatus = crab.Status;
            var oldCondition = crab.Condition;
            var previousBoxId = crab.BoxId;

            crab.Status = CrabStatus.Harvested;
            crab.Condition = CrabCondition.Harvested;
            crab.WeightGram = line.WeightGram;

            // Giữ ảnh thu hoạch trong hồ sơ cua.
            var harvestPhotos = JsonStringList.Parse(voucher.PhotoUrlsJson)
                .Concat(JsonStringList.Parse(line.PhotoUrlsJson))
                .ToList();

            if (harvestPhotos.Count > 0)
            {
                crab.ImageUrlsJson = JsonStringList.Serialize(
                    JsonStringList.Merge(
                        crab.ImageUrlsJson,
                        harvestPhotos,
                        ImageUploadRules.MaxUrls),
                    ImageUploadRules.MaxUrls);
            }

            crab.BoxId = null;
            _uow.Crabs.Update(crab);

            var openAllocations = await _uow.CrabBoxAllocations.FindAsync(
                allocation => allocation.CrabId == crab.Id
                    && allocation.EndTime == null,
                ct);

            foreach (var allocation in openAllocations)
            {
                allocation.EndTime = DateTime.UtcNow;
                _uow.CrabBoxAllocations.Update(allocation);
            }

            if (previousBoxId is Guid boxId)
            {
                var box = await _uow.Boxes.GetByIdAsync(boxId, ct);

                if (box is not null)
                {
                    var hasOtherLiveCrabs = (await _uow.Crabs.FindAsync(
                        other => other.BoxId == boxId
                            && other.Id != crab.Id
                            && (other.Status == CrabStatus.Alive
                                || other.Status == CrabStatus.Molting
                                || other.Status == CrabStatus.Quarantined),
                        ct)).Any();

                    if (!hasOtherLiveCrabs)
                    {
                        var oldBoxStatus = box.Status;
                        var oldIsOccupied = box.IsOccupied;

                        box.IsOccupied = false;
                        box.Status = BoxStatuses.Empty;
                        _uow.Boxes.Update(box);

                        if (!string.Equals(
                                oldBoxStatus,
                                box.Status,
                                StringComparison.OrdinalIgnoreCase)
                            || oldIsOccupied != box.IsOccupied)
                        {
                            await _uow.BoxStatusHistories.AddAsync(
                                new BoxStatusHistory
                                {
                                    BoxId = box.Id,
                                    OldStatus = oldBoxStatus,
                                    NewStatus = box.Status,
                                    OldIsOccupied = oldIsOccupied,
                                    NewIsOccupied = box.IsOccupied,
                                    ChangedAt = DateTime.UtcNow,
                                    Reason = $"Thu hoạch hoàn tất: {voucher.VoucherCode}"
                                },
                                ct);
                        }
                    }
                }
            }

            await _uow.CrabStatusHistories.AddAsync(
                new CrabStatusHistory
                {
                    CrabId = crab.Id,
                    OldStatus = oldStatus,
                    NewStatus = CrabStatus.Harvested,
                    OldCondition = oldCondition,
                    NewCondition = CrabCondition.Harvested,
                    ChangedAt = DateTime.UtcNow,
                    Source = "harvest",
                    Reason = voucher.VoucherCode,
                    ChangedByUserId = _currentUser.UserId
                },
                ct);

            await _uow.CrabHarvestHistories.AddAsync(
                new CrabHarvestHistory
                {
                    CrabId = crab.Id,
                    HarvestLineId = line.Id,
                    HarvestedAt = line.HarvestedAt ?? voucher.HarvestDate,
                    WeightGram = line.WeightGram,
                    Grade = line.Grade,
                    Notes = line.Notes
                },
                ct);
        }
    }
    private async Task<(
        string? CrabCode,
        string? AreaName,
        string? RowName,
        string? BoxCode,
        string? LotCode,
        Guid? BoxId)> SnapshotCrabAsync(
            Guid? crabId,
            CancellationToken ct)
    {
        if (crabId is not Guid id)
            return (null, null, null, null, null, null);

        var crab = await _uow.Crabs.GetByIdAsync(id, ct);
        if (crab is null)
            return (null, null, null, null, null, null);

        string? boxCode = null;
        string? rowName = null;
        string? areaName = null;
        string? lotCode = null;

        if (crab.BoxId is Guid boxId)
        {
            var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
            boxCode = box?.Code;

            if (box is not null)
            {
                var row = await _uow.FarmingRows.GetByIdAsync(
                    box.FarmingRowId,
                    ct);

                rowName = string.IsNullOrWhiteSpace(row?.Name)
                    ? row?.Code
                    : row.Name;

                if (row is not null)
                {
                    var area = await _uow.FarmingAreas.GetByIdAsync(
                        row.FarmingAreaId,
                        ct);

                    areaName = string.IsNullOrWhiteSpace(area?.Name)
                        ? area?.Code
                        : area.Name;
                }
            }
        }

        if (crab.CrabLotId != Guid.Empty)
        {
            var lot = await _uow.CrabLots.GetByIdAsync(crab.CrabLotId, ct);
            lotCode = lot?.LotCode ?? lot?.Name;
        }

        return (crab.Code, areaName, rowName, boxCode, lotCode, crab.BoxId);
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeGrade(string? grade)
        => string.IsNullOrWhiteSpace(grade)
            ? null
            : grade.Trim().ToUpperInvariant();

    private static string NormalizeResult(string? raw)
    {
        var key = (raw ?? "passed").Trim().ToLowerInvariant();

        return key is "failed" or "fail" or "khongdat" or "không đạt" or "reject"
            ? "failed"
            : "passed";
    }

    private async Task<bool> HasActiveHarvestReservationAsync(
        Guid crabId,
        Guid? currentVoucherId,
        CancellationToken ct)
    {
        var voucherIds = (await _uow.HarvestLines.FindAsync(
                line => line.CrabId == crabId
                    && (!currentVoucherId.HasValue
                        || line.HarvestVoucherId != currentVoucherId.Value),
                ct))
            .Select(line => line.HarvestVoucherId)
            .Distinct()
            .ToHashSet();

        if (voucherIds.Count == 0)
            return false;

        return await _uow.HarvestVouchers.AnyAsync(
            voucher => voucherIds.Contains(voucher.Id)
                && voucher.Status != HarvestStatus.Rejected
                && voucher.Status != HarvestStatus.Cancelled,
            ct);
    }
}