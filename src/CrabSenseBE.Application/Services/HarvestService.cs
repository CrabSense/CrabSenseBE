using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Quản lý phiếu thu hoạch, sản lượng và thống kê cua lột.
/// </summary>
public class HarvestService : IHarvestService
{
    private static readonly HashSet<string> AllowedGrades =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "S",
            "M",
            "L",
            "XL",
            "XXL",
            "A",
            "B",
            "C"
        };

    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;

    public HarvestService(
        IUnitOfWork uow,
        ICurrentUserService currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<HarvestVoucherDetailDto>> GetByIdAsync(
    Guid id,
    CancellationToken ct = default)
    {
        var voucher = await GetVoucherOrThrowAsync(id, ct);

    var lines = await _uow.HarvestLines.FindAsync(
        line => line.HarvestVoucherId == id,
        ct);

    return ApiResponse<HarvestVoucherDetailDto>.Ok(
        MapVoucherDetail(voucher, lines, await AreaNameAsync(voucher.FarmingAreaId, ct)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Query
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<HarvestVoucherDto>>> GetAllAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        var vouchers = (await _uow.HarvestVouchers.GetAllAsync(ct))
            .Where(voucher =>
                farmingAreaId is null
                || farmingAreaId == Guid.Empty
                || voucher.FarmingAreaId == farmingAreaId)
            .OrderByDescending(voucher => voucher.HarvestDate)
            .ThenByDescending(voucher => voucher.CreatedAt)
            .ToList();

        var voucherIds = vouchers
            .Select(voucher => voucher.Id)
            .ToList();

        var lines = voucherIds.Count == 0
            ? new List<HarvestLine>()
            : (
                await _uow.HarvestLines.FindAsync(
                    line => voucherIds.Contains(line.HarvestVoucherId),
                    ct))
                .ToList();

        var linesByVoucher = lines
            .GroupBy(line => line.HarvestVoucherId)
            .ToDictionary(
                group => group.Key,
                group => group.ToList());

        var areaNames = await LoadAreaNamesAsync(
            vouchers.Select(v => v.FarmingAreaId),
            ct);

        var result = vouchers
            .Select(voucher =>
            {
                var voucherLines = linesByVoucher.TryGetValue(
                    voucher.Id,
                    out var foundLines)
                        ? foundLines
                        : Enumerable.Empty<HarvestLine>();

                return MapVoucher(
                    voucher,
                    voucherLines,
                    areaNames.GetValueOrDefault(voucher.FarmingAreaId ?? Guid.Empty));
            })
            .ToList();

        return ApiResponse<IEnumerable<HarvestVoucherDto>>.Ok(result);
    }

    public async Task<ApiResponse<HarvestOverviewDto>> GetOverviewAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        var boxIdsInArea = await BoxIdsInAreaAsync(farmingAreaId, ct);
        var crabs = (await _uow.Crabs.GetAllAsync(ct)).ToList();
        bool InArea(Crab c) =>
            boxIdsInArea is null
            || (c.BoxId is Guid bid && boxIdsInArea.Contains(bid));

        var harvestableCrabs = crabs
            .Where(c =>
                (c.Status is CrabStatus.Alive or CrabStatus.Molting or CrabStatus.Quarantined)
                && InArea(c))
            .ToList();
        var harvestable = harvestableCrabs.Count;
        var softshellWaiting = harvestableCrabs.Count(c =>
            c.Condition is CrabCondition.Softshell or CrabCondition.Molting
            || c.Status == CrabStatus.Molting
            || c.MoltingStage.Contains("soft", StringComparison.OrdinalIgnoreCase)
            || c.MoltingStage.Contains("lột", StringComparison.OrdinalIgnoreCase));

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var areaVouchers = (await _uow.HarvestVouchers.GetAllAsync(ct))
            .Where(v =>
                farmingAreaId is null
                || farmingAreaId == Guid.Empty
                || v.FarmingAreaId == farmingAreaId)
            .ToList();
        var todayCompleted = areaVouchers
            .Where(v =>
                v.Status == HarvestStatus.Completed
                && v.HarvestDate >= today
                && v.HarvestDate < tomorrow)
            .ToList();
        var harvestedToday = todayCompleted.Sum(v => v.TotalQuantity);
        var todayWeightKg = decimal.Round(
            todayCompleted.Sum(v => v.TotalWeightKg),
            3);

        var completedIds = areaVouchers
            .Where(v => v.Status == HarvestStatus.Completed)
            .Select(v => v.Id)
            .ToHashSet();
        var harvestedCrabIds = completedIds.Count == 0
            ? new HashSet<Guid>()
            : (await _uow.HarvestLines.FindAsync(
                    l => l.CrabId != null && completedIds.Contains(l.HarvestVoucherId),
                    ct))
                .Select(l => l.CrabId!.Value)
                .ToHashSet();
        var waitingCrabs = crabs
            .Where(c => c.Status == CrabStatus.Harvested
                && (boxIdsInArea is null || harvestedCrabIds.Contains(c.Id)))
            .ToList();

        return ApiResponse<HarvestOverviewDto>.Ok(new HarvestOverviewDto(
            harvestable,
            harvestedToday,
            waitingCrabs.Count,
            todayWeightKg,
            softshellWaiting));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Create
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<HarvestVoucherDetailDto>> CreateAsync(
        CreateHarvestVoucherRequest request,
        CancellationToken ct = default)
    {
        var requestLines = request.Lines?.ToList() ?? [];

        if (request.HarvestDate == default)
        {
            throw AppException.BadRequest(
                "HarvestDate is required.");
        }

        if (request.HarvestDate.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5))
        {
            throw AppException.BadRequest(
                "HarvestDate cannot be in the future.");
        }

        var suppliedCrabIds = requestLines
            .Where(line => line.CrabId.HasValue)
            .Select(line => line.CrabId!.Value)
            .ToList();

        var statusSpecified = !string.IsNullOrWhiteSpace(request.Status);
        HarvestStatus status;
        if (statusSpecified)
        {
            if (!Enum.TryParse<HarvestStatus>(
                    request.Status!.Trim(),
                    ignoreCase: true,
                    out status))
            {
                var allowed = string.Join(", ", Enum.GetNames<HarvestStatus>());
                throw AppException.BadRequest(
                    $"Invalid harvest status. Allowed values: {allowed}.");
            }
        }
        else
        {
            // Clients cũ: tạo phiếu kèm cua = hoàn tất ngay.
            status = suppliedCrabIds.Count > 0
                ? HarvestStatus.Completed
                : HarvestStatus.Planned;
        }

        var completeNow = status == HarvestStatus.Completed;
        var requireWeights = status is HarvestStatus.InProgress
            or HarvestStatus.Completed;

        if (requireWeights && requestLines.Count == 0)
        {
            throw AppException.BadRequest(
                "At least one harvest line is required.");
        }

        if (status == HarvestStatus.Planned
            && request.FarmingAreaId is null
            && suppliedCrabIds.Count == 0)
        {
            throw AppException.BadRequest(
                "FarmingAreaId is required when saving a draft without crabs.");
        }

        if (requestLines.Count > 0)
        {
            ValidateHarvestLines(requestLines, requirePositiveWeight: requireWeights);
            await ValidateCrabsAsync(suppliedCrabIds, ct);
        }

        var areaId = request.FarmingAreaId;
        if (areaId is null && suppliedCrabIds.Count > 0)
            areaId = await InferAreaFromCrabAsync(suppliedCrabIds[0], ct);

        var performer = NormalizeNullable(request.PerformedByName)
            ?? await ResolveUserNameAsync(ct);

        var voucher = new HarvestVoucher
        {
            VoucherCode = await GenerateVoucherCodeAsync(ct),
            // CropBatchId = request.CropBatchId,
            HarvestDate = NormalizeUtc(request.HarvestDate),
            Status = status,
            Notes = NormalizeNullable(request.Notes),
            CreatedBy = _currentUser.UserId,
            FarmingAreaId = areaId,
            PerformedByName = performer,
            PhotoUrlsJson = JsonStringList.Serialize(request.PhotoUrls)
        };

        foreach (var requestLine in requestLines)
        {
            var snap = await SnapshotCrabAsync(requestLine.CrabId, ct);
            voucher.Lines.Add(new HarvestLine
            {
                HarvestVoucherId = voucher.Id,
                CrabId = requestLine.CrabId,
                BoxId = snap.BoxId,
                WeightGram = requestLine.WeightGram,
                Grade = NormalizeGrade(requestLine.Grade),
                IsSoftshell = requestLine.IsSoftshell,
                Notes = NormalizeNullable(requestLine.Notes),
                ConditionLabel = NormalizeNullable(requestLine.ConditionLabel),
                PhotoUrlsJson = JsonStringList.Serialize(requestLine.PhotoUrls),
                CrabCode = snap.CrabCode,
                AreaName = snap.AreaName,
                RowName = snap.RowName,
                BoxCode = snap.BoxCode,
                LotCode = snap.LotCode,
                Result = NormalizeResult(requestLine.Result)
            });
        }

        voucher.TotalQuantity = voucher.Lines.Count;
        voucher.TotalWeightKg = CalculateTotalWeightKg(voucher.Lines);

        await _uow.HarvestVouchers.AddAsync(voucher, ct);

        if (completeNow)
            await ApplyHarvestToCrabsAsync(voucher, ct);

        await _uow.SaveChangesAsync(ct);

        return ApiResponse<HarvestVoucherDetailDto>.Ok(
            MapVoucherDetail(voucher, voucher.Lines, await AreaNameAsync(voucher.FarmingAreaId, ct)),
            $"Harvest voucher '{voucher.VoucherCode}' created.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Status
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<HarvestVoucherDetailDto>> UpdateStatusAsync(
        Guid id,
        UpdateHarvestStatusRequest request,
        CancellationToken ct = default)
    {
        var voucher = await GetVoucherOrThrowAsync(id, ct);

        if (string.IsNullOrWhiteSpace(request.Status))
        {
            throw AppException.BadRequest(
                "Status is required.");
        }

        if (!Enum.TryParse<HarvestStatus>(
                request.Status.Trim(),
                ignoreCase: true,
                out var targetStatus))
        {
            var allowed = string.Join(
                ", ",
                Enum.GetNames<HarvestStatus>());

            throw AppException.BadRequest(
                $"Invalid harvest status. Allowed values: {allowed}.");
        }

        ValidateStatusTransition(voucher.Status, targetStatus);

        voucher.Status = targetStatus;
        _uow.HarvestVouchers.Update(voucher);

        if (targetStatus == HarvestStatus.Completed)
        {
            var pendingLines = await _uow.HarvestLines.FindAsync(
                line => line.HarvestVoucherId == voucher.Id,
                ct);
            voucher.Lines = pendingLines.ToList();
            await ApplyHarvestToCrabsAsync(voucher, ct);
        }

        await _uow.SaveChangesAsync(ct);

        var lines = await _uow.HarvestLines.FindAsync(
            line => line.HarvestVoucherId == voucher.Id,
            ct);

        return ApiResponse<HarvestVoucherDetailDto>.Ok(
            MapVoucherDetail(voucher, lines, await AreaNameAsync(voucher.FarmingAreaId, ct)),
            $"Harvest voucher status updated to '{targetStatus}'.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Delete
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse> DeleteAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var voucher = await GetVoucherOrThrowAsync(id, ct);

        if (voucher.Status is not HarvestStatus.Planned
            and not HarvestStatus.Cancelled)
        {
            throw AppException.Conflict(
                "Only Planned or Cancelled harvest vouchers can be deleted.");
        }

        var hasFrozenLots = await _uow.FrozenLots.AnyAsync(
            lot => lot.HarvestVoucherId == id,
            ct);

        if (hasFrozenLots)
        {
            throw AppException.Conflict(
                $"Cannot delete harvest voucher '{voucher.VoucherCode}' " +
                "because frozen lots reference it.");
        }

        var lines = await _uow.HarvestLines.FindAsync(
            line => line.HarvestVoucherId == id,
            ct);

        foreach (var line in lines)
        {
            _uow.HarvestLines.Remove(line);
        }

        _uow.HarvestVouchers.Remove(voucher);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse.Ok(
            $"Harvest voucher '{voucher.VoucherCode}' deleted.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Statistics
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<HarvestStatisticsDto>> GetStatisticsAsync(
        DateTime from,
        DateTime to,
        string period,
        CancellationToken ct = default)
    {
        var normalizedPeriod = NormalizePeriod(period);
        var fromUtc = NormalizeUtc(from);
        var toUtc = NormalizeUtc(to);

        if (from == default || to == default)
        {
            throw AppException.BadRequest(
                "Both from and to are required.");
        }

        if (toUtc <= fromUtc)
        {
            throw AppException.BadRequest(
                "'to' must be later than 'from'.");
        }

        var vouchers = (
            await _uow.HarvestVouchers.FindAsync(
                voucher =>
                    voucher.Status == HarvestStatus.Completed
                    && voucher.HarvestDate >= fromUtc
                    && voucher.HarvestDate < toUtc,
                ct))
            .ToList();

        var voucherIds = vouchers
            .Select(voucher => voucher.Id)
            .ToList();

        var lines = voucherIds.Count == 0
            ? new List<HarvestLine>()
            : (
                await _uow.HarvestLines.FindAsync(
                    line => voucherIds.Contains(line.HarvestVoucherId),
                    ct))
                .ToList();

        var linesByVoucher = lines
            .GroupBy(line => line.HarvestVoucherId)
            .ToDictionary(
                group => group.Key,
                group => group.ToList());

        var items = vouchers
            .GroupBy(voucher =>
                GetPeriodStart(voucher.HarvestDate, normalizedPeriod))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var periodVouchers = group.ToList();

                var periodLines = periodVouchers
                    .SelectMany(voucher =>
                        linesByVoucher.TryGetValue(voucher.Id, out var voucherLines)
                            ? voucherLines
                            : Enumerable.Empty<HarvestLine>())
                    .ToList();

                var softshellQuantity = periodLines.Count(
                    line => line.IsSoftshell);

                var totalQuantity = periodVouchers.Sum(
                    voucher => voucher.TotalQuantity);

                return new HarvestPeriodItemDto(
                    PeriodStart: group.Key,
                    PeriodEnd: GetPeriodEnd(group.Key, normalizedPeriod),
                    VoucherCount: periodVouchers.Count,
                    TotalQuantity: totalQuantity,
                    TotalWeightKg: decimal.Round(
                        periodVouchers.Sum(voucher => voucher.TotalWeightKg),
                        3),
                    SoftshellQuantity: softshellQuantity,
                    SoftshellRate: CalculateRate(
                        softshellQuantity,
                        totalQuantity));
            })
            .ToList();

        var totalSoftshellQuantity = lines.Count(line => line.IsSoftshell);
        var totalQuantity = vouchers.Sum(voucher => voucher.TotalQuantity);

        var result = new HarvestStatisticsDto(
            From: fromUtc,
            To: toUtc,
            Period: normalizedPeriod,
            VoucherCount: vouchers.Count,
            TotalQuantity: totalQuantity,
            TotalWeightKg: decimal.Round(
                vouchers.Sum(voucher => voucher.TotalWeightKg),
                3),
            SoftshellQuantity: totalSoftshellQuantity,
            SoftshellRate: CalculateRate(
                totalSoftshellQuantity,
                totalQuantity),
            Items: items);

        return ApiResponse<HarvestStatisticsDto>.Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Box ↔ Harvest link
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<HarvestBoxDto>>> GetBoxesByVoucherAsync(
        Guid voucherId,
        CancellationToken ct = default)
    {
        // Đảm bảo phiếu thu hoạch tồn tại
        await GetVoucherOrThrowAsync(voucherId, ct);

        var lines = await _uow.HarvestLines
            .Query()
            .Include(l => l.Box)
            .Where(l => l.HarvestVoucherId == voucherId)
            .ToListAsync(ct);

        // Nhóm theo BoxId → mỗi box một dòng
        var boxes = lines
            .Where(l => l.BoxId.HasValue)
            .GroupBy(l => l.BoxId!.Value)
            .Select(g => new HarvestBoxDto(
                BoxId: g.Key,
                BoxCode: g.First().Box?.Code ?? "N/A",
                CrabCount: g.Count(),
                TotalWeightGram: decimal.Round(g.Sum(l => l.WeightGram), 2)))
            .OrderBy(b => b.BoxCode)
            .ToList();

        return ApiResponse<IEnumerable<HarvestBoxDto>>.Ok(boxes);
    }

    public async Task<ApiResponse<IEnumerable<BoxHarvestVoucherDto>>> GetVouchersByBoxAsync(
        Guid boxId,
        CancellationToken ct = default)
    {
        // Đảm bảo box tồn tại
        _ = await _uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");

        var lines = await _uow.HarvestLines
            .Query()
            .Include(l => l.HarvestVoucher)
            .Where(l => l.BoxId == boxId)
            .ToListAsync(ct);

        // Nhóm theo VoucherId → mỗi phiếu thu hoạch một dòng
        var vouchers = lines
            .GroupBy(l => l.HarvestVoucherId)
            .Select(g =>
            {
                var voucher = g.First().HarvestVoucher!;
                return new BoxHarvestVoucherDto(
                    VoucherId: voucher.Id,
                    VoucherCode: voucher.VoucherCode,
                    HarvestDate: voucher.HarvestDate,
                    Status: voucher.Status.ToString(),
                    CrabCount: g.Count(),
                    TotalWeightGram: decimal.Round(g.Sum(l => l.WeightGram), 2));
            })
            .OrderByDescending(v => v.HarvestDate)
            .ToList();

        return ApiResponse<IEnumerable<BoxHarvestVoucherDto>>.Ok(vouchers);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Validation
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateHarvestLines(
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
                $"Crab '{duplicateCrabId.Key}' appears more than once " +
                "in the harvest request.");
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
                    $"Invalid grade at line {lineNumber}. " +
                    "Allowed values: S, M, L, XL, XXL, A, B, C.");
            }
        }
    }

    private async Task ValidateCrabsAsync(
        IReadOnlyCollection<Guid> crabIds,
        // Guid? cropBatchId,
        CancellationToken ct)
    {
        foreach (var crabId in crabIds)
        {
            var crab = await _uow.Crabs.GetByIdAsync(crabId, ct)
                ?? throw AppException.BadRequest(
                    $"Crab '{crabId}' does not exist.");

            if (crab.Status == CrabStatus.Dead || crab.Status == CrabStatus.Harvested || crab.Status == CrabStatus.Missing)
            {
                throw AppException.Conflict(
                    $"Crab '{crabId}' is not alive and cannot be harvested.");
            }

            // if (cropBatchId.HasValue
            //     && crab.CropBatchId != cropBatchId.Value)
            // {
            //     throw AppException.Conflict(
            //         $"Crab '{crabId}' does not belong to crop batch " +
            //         $"'{cropBatchId.Value}'.");
            // }

            var alreadyHarvested = await _uow.HarvestLines.AnyAsync(
                line => line.CrabId == crabId,
                ct);

            if (alreadyHarvested)
            {
                throw AppException.Conflict(
                    $"Crab '{crabId}' has already been recorded " +
                    "in another harvest voucher.");
            }
        }
    }

    private static void ValidateStatusTransition(
        HarvestStatus current,
        HarvestStatus target)
    {
        if (current == target)
        {
            throw AppException.Conflict(
                $"Harvest voucher is already in status '{current}'.");
        }

        var allowed = current switch
        {
            HarvestStatus.Planned =>
                target is HarvestStatus.InProgress
                    or HarvestStatus.Cancelled,

            HarvestStatus.InProgress =>
                target is HarvestStatus.Completed
                    or HarvestStatus.Cancelled,

            HarvestStatus.Completed => false,
            HarvestStatus.Cancelled => false,
            _ => false
        };

        if (!allowed)
        {
            throw AppException.Conflict(
                $"Cannot change harvest status from '{current}' to '{target}'.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mapping
    // ─────────────────────────────────────────────────────────────────────────

    private static HarvestVoucherDto MapVoucher(
        HarvestVoucher voucher,
        IEnumerable<HarvestLine> lines,
        string? areaName = null)
    {
        var softshellQuantity = lines.Count(
            line => line.IsSoftshell);

    var lineDtos = lines.Select(MapLine).ToList();
    var passed = lineDtos.Count(l => IsPassed(l.Result));
    var failed = lineDtos.Count - passed;
    var avg = voucher.TotalQuantity <= 0
        ? 0
        : decimal.Round(voucher.TotalWeightKg * 1000m / voucher.TotalQuantity, 1);

    return new HarvestVoucherDto(
        Id: voucher.Id,
        VoucherCode: voucher.VoucherCode,
        // CropBatchId: voucher.CropBatchId,
        HarvestDate: voucher.HarvestDate,
        Status: voucher.Status.ToString(),
        TotalQuantity: voucher.TotalQuantity,
        TotalWeightKg: voucher.TotalWeightKg,
        SoftshellQuantity: softshellQuantity,
        SoftshellRate: CalculateRate(
            softshellQuantity,
            voucher.TotalQuantity),
        Notes: voucher.Notes,
        CreatedBy: voucher.CreatedBy,
        CreatedAt: voucher.CreatedAt,
        FarmingAreaId: voucher.FarmingAreaId,
        PerformedByName: voucher.PerformedByName,
        AreaName: areaName,
        PassedCount: passed,
        FailedCount: failed,
        AverageWeightGram: avg,
        PhotoUrls: JsonStringList.Parse(voucher.PhotoUrlsJson),
        Lines: lineDtos);
    }

    private static HarvestVoucherDetailDto MapVoucherDetail(
        HarvestVoucher voucher,
        IEnumerable<HarvestLine> lines,
        string? areaName = null)
    {
        var lineList = lines
            .OrderBy(line => line.CreatedAt)
            .Select(MapLine)
            .ToList();

        var softshellQuantity = lineList.Count(
            line => line.IsSoftshell);
        var passed = lineList.Count(l => IsPassed(l.Result));
        var failed = lineList.Count - passed;
        var avg = voucher.TotalQuantity <= 0
            ? 0
            : decimal.Round(voucher.TotalWeightKg * 1000m / voucher.TotalQuantity, 1);

        return new HarvestVoucherDetailDto(
            Id: voucher.Id,
            VoucherCode: voucher.VoucherCode,
            // CropBatchId: voucher.CropBatchId,
            HarvestDate: voucher.HarvestDate,
            Status: voucher.Status.ToString(),
            TotalQuantity: voucher.TotalQuantity,
            TotalWeightKg: voucher.TotalWeightKg,
            SoftshellQuantity: softshellQuantity,
            SoftshellRate: CalculateRate(
                softshellQuantity,
                voucher.TotalQuantity),
            Notes: voucher.Notes,
            CreatedBy: voucher.CreatedBy,
            CreatedAt: voucher.CreatedAt,
            Lines: lineList,
            FarmingAreaId: voucher.FarmingAreaId,
            PerformedByName: voucher.PerformedByName,
            AreaName: areaName,
            PassedCount: passed,
            FailedCount: failed,
            AverageWeightGram: avg,
            PhotoUrls: JsonStringList.Parse(voucher.PhotoUrlsJson));
    }

    private static HarvestLineDto MapLine(
        HarvestLine line)
    {
        return new HarvestLineDto(
            Id: line.Id,
            CrabId: line.CrabId,
            BoxId: line.BoxId,
            BoxCode: line.BoxCode ?? line.Box?.Code,
            WeightGram: line.WeightGram,
            Grade: line.Grade,
            IsSoftshell: line.IsSoftshell,
            Notes: line.Notes,
            CrabCode: line.CrabCode,
            ConditionLabel: line.ConditionLabel,
            PhotoUrls: JsonStringList.Parse(line.PhotoUrlsJson),
            AreaName: line.AreaName,
            RowName: line.RowName,
            LotCode: line.LotCode,
            Result: line.Result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<HarvestVoucher> GetVoucherOrThrowAsync(
        Guid id,
        CancellationToken ct)
    {
        return await _uow.HarvestVouchers.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("HarvestVoucher");
    }

    private async Task<string> GenerateVoucherCodeAsync(
        CancellationToken ct)
    {
        var codes = (await _uow.HarvestVouchers.GetAllAsync(ct))
            .Select(v => v.VoucherCode)
            .Where(c => c.StartsWith("HAR-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var max = 0;
        foreach (var code in codes)
        {
            var tail = code["HAR-".Length..];
            if (int.TryParse(tail, out var n) && n > max) max = n;
        }

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            var next = $"HAR-{(max + attempt):000}";
            if (!await _uow.HarvestVouchers.AnyAsync(v => v.VoucherCode == next, ct))
                return next;
        }

        throw AppException.Conflict(
            "Unable to generate a unique harvest voucher code.");
    }

    private static string NormalizeResult(string? raw)
    {
        var key = (raw ?? "passed").Trim().ToLowerInvariant();
        if (key is "failed" or "fail" or "khongdat" or "không đạt" or "reject")
            return "failed";
        return "passed";
    }

    private static bool IsPassed(string? result) =>
        !string.Equals(result, "failed", StringComparison.OrdinalIgnoreCase);

    private async Task<(string? CrabCode, string? AreaName, string? RowName, string? BoxCode, string? LotCode, Guid? BoxId)>
        SnapshotCrabAsync(Guid? crabId, CancellationToken ct)
    {
        if (crabId is not Guid id) return (null, null, null, null, null, null);
        var crab = await _uow.Crabs.GetByIdAsync(id, ct);
        if (crab is null) return (null, null, null, null, null, null);

        string? boxCode = null, rowName = null, areaName = null, lotCode = null;
        if (crab.BoxId is Guid boxId)
        {
            var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
            boxCode = box?.Code;
            if (box is not null)
            {
                var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
                rowName = string.IsNullOrWhiteSpace(row?.Name) ? row?.Code : row?.Name;
                if (row is not null)
                {
                    var area = await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);
                    areaName = string.IsNullOrWhiteSpace(area?.Name) ? area?.Code : area?.Name;
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

    private static decimal CalculateTotalWeightKg(
        IEnumerable<HarvestLine> lines)
    {
        return decimal.Round(
            lines.Sum(line => line.WeightGram) / 1000m,
            3,
            MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateRate(
        int softshellQuantity,
        int totalQuantity)
    {
        if (totalQuantity <= 0)
        {
            return 0m;
        }

        return decimal.Round(
            softshellQuantity * 100m / totalQuantity,
            2,
            MidpointRounding.AwayFromZero);
    }

    private static string NormalizePeriod(
        string period)
    {
        if (string.IsNullOrWhiteSpace(period))
        {
            throw AppException.BadRequest(
                "Period is required. Allowed values: day, week, month.");
        }

        var normalized = period.Trim().ToLowerInvariant();

        if (normalized is not "day"
            and not "week"
            and not "month")
        {
            throw AppException.BadRequest(
                "Invalid period. Allowed values: day, week, month.");
        }

        return normalized;
    }

    private static DateTime GetPeriodStart(
        DateTime value,
        string period)
    {
        var date = value.Date;

        return period switch
        {
            "day" => DateTime.SpecifyKind(
                date,
                DateTimeKind.Utc),

            "week" => DateTime.SpecifyKind(
                date.AddDays(
                    -(((int)date.DayOfWeek + 6) % 7)),
                DateTimeKind.Utc),

            "month" => new DateTime(
                date.Year,
                date.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc),

            _ => throw AppException.BadRequest(
                "Invalid statistics period.")
        };
    }

    private static DateTime GetPeriodEnd(
        DateTime periodStart,
        string period)
    {
        return period switch
        {
            "day" => periodStart.AddDays(1),
            "week" => periodStart.AddDays(7),
            "month" => periodStart.AddMonths(1),
            _ => throw AppException.BadRequest(
                "Invalid statistics period.")
        };
    }

    private static DateTime NormalizeUtc(
        DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string? NormalizeNullable(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? NormalizeGrade(
        string? grade)
    {
        return string.IsNullOrWhiteSpace(grade)
            ? null
            : grade.Trim().ToUpperInvariant();
    }

    private async Task ApplyHarvestToCrabsAsync(HarvestVoucher voucher, CancellationToken ct)
    {
        foreach (var line in voucher.Lines.Where(l => l.CrabId.HasValue))
        {
            var crab = await _uow.Crabs.GetByIdAsync(line.CrabId!.Value, ct);
            if (crab is null) continue;
            if (crab.Status is CrabStatus.Harvested or CrabStatus.Sold)
                continue;

            var oldStatus = crab.Status;
            var oldCondition = crab.Condition;
            var previousBoxId = crab.BoxId;

            crab.Status = CrabStatus.Harvested;
            crab.Condition = CrabCondition.Harvested;
            crab.WeightGram = line.WeightGram;
            crab.BoxId = null;
            _uow.Crabs.Update(crab);

            var open = await _uow.CrabBoxAllocations.FindAsync(
                a => a.CrabId == crab.Id && a.EndTime == null, ct);
            foreach (var alloc in open)
            {
                alloc.EndTime = DateTime.UtcNow;
                _uow.CrabBoxAllocations.Update(alloc);
            }

            if (previousBoxId is Guid boxId)
            {
                var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
                if (box is not null)
                {
                    var stillLive = (await _uow.Crabs.FindAsync(
                            c => c.BoxId == boxId
                                 && c.Id != crab.Id
                                 && (c.Status == CrabStatus.Alive
                                     || c.Status == CrabStatus.Molting
                                     || c.Status == CrabStatus.Quarantined),
                            ct))
                        .Any();
                    if (!stillLive)
                    {
                        box.IsOccupied = false;
                        box.Status = "empty";
                        _uow.Boxes.Update(box);
                    }
                }
            }

            await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
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
            }, ct);

            await _uow.CrabHarvestHistories.AddAsync(new CrabHarvestHistory
            {
                CrabId = crab.Id,
                HarvestLineId = line.Id,
                HarvestedAt = voucher.HarvestDate,
                WeightGram = line.WeightGram,
                Grade = line.Grade,
                Notes = line.Notes
            }, ct);
        }
    }

    private async Task<Guid?> InferAreaFromCrabAsync(Guid crabId, CancellationToken ct)
    {
        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct);
        if (crab?.BoxId is not Guid boxId) return null;
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
        if (box is null) return null;
        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
        return row?.FarmingAreaId;
    }

    private async Task<string?> AreaNameAsync(Guid? areaId, CancellationToken ct)
    {
        if (areaId is not Guid id || id == Guid.Empty) return null;
        var area = await _uow.FarmingAreas.GetByIdAsync(id, ct);
        if (area is null) return null;
        return string.IsNullOrWhiteSpace(area.Name) ? area.Code : area.Name;
    }

    private async Task<Dictionary<Guid, string>> LoadAreaNamesAsync(
        IEnumerable<Guid?> ids,
        CancellationToken ct)
    {
        var set = ids.Where(id => id is Guid g && g != Guid.Empty)
            .Select(id => id!.Value)
            .ToHashSet();
        if (set.Count == 0) return new Dictionary<Guid, string>();
        var areas = await _uow.FarmingAreas.GetAllAsync(ct);
        return areas
            .Where(a => set.Contains(a.Id))
            .ToDictionary(
                a => a.Id,
                a => string.IsNullOrWhiteSpace(a.Name) ? a.Code : a.Name);
    }

    private async Task<HashSet<Guid>?> BoxIdsInAreaAsync(Guid? farmingAreaId, CancellationToken ct)
    {
        if (farmingAreaId is not Guid areaId || areaId == Guid.Empty)
            return null;
        var rowIds = (await _uow.FarmingRows.FindAsync(r => r.FarmingAreaId == areaId, ct))
            .Select(r => r.Id)
            .ToHashSet();
        return (await _uow.Boxes.GetAllAsync(ct))
            .Where(b => rowIds.Contains(b.FarmingRowId))
            .Select(b => b.Id)
            .ToHashSet();
    }

    private async Task<string> ResolveUserNameAsync(CancellationToken ct)
    {
        try
        {
            var user = await _uow.Users.GetByIdAsync(_currentUser.UserId, ct);
            if (user is not null && !string.IsNullOrWhiteSpace(user.FullName))
                return user.FullName.Trim();
        }
        catch
        {
            // JWT without user row
        }

        return "Owner";
    }
}