using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

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
            "L"
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
        MapVoucherDetail(voucher, lines));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Query
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<HarvestVoucherDto>>> GetAllAsync(
    CancellationToken ct = default)
    {
    var vouchers = (
        await _uow.HarvestVouchers.GetAllAsync(ct))
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
            group => group.AsEnumerable());

    var result = vouchers
        .Select(voucher =>
        {
            var voucherLines = linesByVoucher.TryGetValue(
                voucher.Id,
                out var foundLines)
                    ? foundLines
                    : Enumerable.Empty<HarvestLine>();

            return MapVoucher(voucher, voucherLines);
        })
        .ToList();

    return ApiResponse<IEnumerable<HarvestVoucherDto>>.Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Create
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<HarvestVoucherDetailDto>> CreateAsync(
        CreateHarvestVoucherRequest request,
        CancellationToken ct = default)
    {
        var requestLines = request.Lines?.ToList()
            ?? throw AppException.BadRequest(
                "Harvest lines are required.");

        if (requestLines.Count == 0)
        {
            throw AppException.BadRequest(
                "At least one harvest line is required.");
        }

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

        if (request.CropBatchId.HasValue)
        {
            var cropBatchExists = await _uow.CropBatches.AnyAsync(
                batch => batch.Id == request.CropBatchId.Value,
                ct);

            if (!cropBatchExists)
            {
                throw AppException.BadRequest(
                    $"CropBatch '{request.CropBatchId}' does not exist.");
            }
        }

        ValidateHarvestLines(requestLines);

        var suppliedCrabIds = requestLines
            .Where(line => line.CrabId.HasValue)
            .Select(line => line.CrabId!.Value)
            .ToList();

        await ValidateCrabsAsync(
            suppliedCrabIds,
            request.CropBatchId,
            ct);

        var voucher = new HarvestVoucher
        {
            VoucherCode = await GenerateVoucherCodeAsync(ct),
            CropBatchId = request.CropBatchId,
            HarvestDate = NormalizeUtc(request.HarvestDate),
            Status = HarvestStatus.Planned,
            Notes = NormalizeNullable(request.Notes),
            CreatedBy = _currentUser.UserId
        };

        foreach (var requestLine in requestLines)
        {
            voucher.Lines.Add(new HarvestLine
            {
                HarvestVoucherId = voucher.Id,
                CrabId = requestLine.CrabId,
                WeightGram = requestLine.WeightGram,
                Grade = NormalizeGrade(requestLine.Grade),
                IsSoftshell = requestLine.IsSoftshell,
                Notes = NormalizeNullable(requestLine.Notes)
            });
        }

        voucher.TotalQuantity = voucher.Lines.Count;
        voucher.TotalWeightKg = CalculateTotalWeightKg(voucher.Lines);

        await _uow.HarvestVouchers.AddAsync(voucher, ct);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<HarvestVoucherDetailDto>.Ok(
            MapVoucherDetail(voucher, voucher.Lines),
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
        await _uow.SaveChangesAsync(ct);

        var lines = await _uow.HarvestLines.FindAsync(
            line => line.HarvestVoucherId == voucher.Id,
            ct);

        return ApiResponse<HarvestVoucherDetailDto>.Ok(
            MapVoucherDetail(voucher, lines),
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
    // Validation
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateHarvestLines(
        IReadOnlyCollection<HarvestLineRequest> lines)
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

            if (line.WeightGram <= 0)
            {
                throw AppException.BadRequest(
                    $"WeightGram at line {lineNumber} must be greater than zero.");
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
                    "Allowed values: S, M, L.");
            }
        }
    }

    private async Task ValidateCrabsAsync(
        IReadOnlyCollection<Guid> crabIds,
        Guid? cropBatchId,
        CancellationToken ct)
    {
        foreach (var crabId in crabIds)
        {
            var crab = await _uow.Crabs.GetByIdAsync(crabId, ct)
                ?? throw AppException.BadRequest(
                    $"Crab '{crabId}' does not exist.");

            if (!crab.IsAlive)
            {
                throw AppException.Conflict(
                    $"Crab '{crabId}' is not alive and cannot be harvested.");
            }

            if (cropBatchId.HasValue
                && crab.CropBatchId != cropBatchId.Value)
            {
                throw AppException.Conflict(
                    $"Crab '{crabId}' does not belong to crop batch " +
                    $"'{cropBatchId.Value}'.");
            }

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
    IEnumerable<HarvestLine> lines)
    {
    var softshellQuantity = lines.Count(
        line => line.IsSoftshell);

    return new HarvestVoucherDto(
        Id: voucher.Id,
        VoucherCode: voucher.VoucherCode,
        CropBatchId: voucher.CropBatchId,
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
        CreatedAt: voucher.CreatedAt);
    }

    private static HarvestVoucherDetailDto MapVoucherDetail(
        HarvestVoucher voucher,
        IEnumerable<HarvestLine> lines)
    {
        var lineList = lines
            .OrderBy(line => line.CreatedAt)
            .Select(MapLine)
            .ToList();

        var softshellQuantity = lineList.Count(
            line => line.IsSoftshell);

        return new HarvestVoucherDetailDto(
            Id: voucher.Id,
            VoucherCode: voucher.VoucherCode,
            CropBatchId: voucher.CropBatchId,
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
            Lines: lineList);
    }

    private static HarvestLineDto MapLine(
        HarvestLine line)
    {
        return new HarvestLineDto(
            Id: line.Id,
            CrabId: line.CrabId,
            WeightGram: line.WeightGram,
            Grade: line.Grade,
            IsSoftshell: line.IsSoftshell,
            Notes: line.Notes);
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
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var suffix = Guid.NewGuid()
                .ToString("N")[..6]
                .ToUpperInvariant();

            var code = $"HV-{DateTime.UtcNow:yyyyMMdd}-{suffix}";

            var exists = await _uow.HarvestVouchers.AnyAsync(
                voucher => voucher.VoucherCode == code,
                ct);

            if (!exists)
            {
                return code;
            }
        }

        throw AppException.Conflict(
            "Unable to generate a unique harvest voucher code.");
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
}
