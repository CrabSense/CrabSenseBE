using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>Phiếu nhập lô cua. Số cua đã thả (PlacedCount) tách khỏi Quantity khai báo.</summary>
public class FarmLotService : IFarmLotService
{
    private readonly IUnitOfWork _uow;

    public FarmLotService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<IEnumerable<CrabLotDto>>> GetLotsAsync(CancellationToken ct = default)
    {
        var lots = await _uow.CrabLots.GetAllAsync(ct);
        var placed = await LoadPlacedCountsAsync(ct);
        return ApiResponse<IEnumerable<CrabLotDto>>.Ok(
            lots.OrderByDescending(l => l.ImportDate).Select(l => MapLot(l, placed)));
    }

    public async Task<ApiResponse<CrabLotDto>> GetLotByIdAsync(Guid id, CancellationToken ct = default)
    {
        var lot = await _uow.CrabLots.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("CrabLot");
        var placed = await LoadPlacedCountsAsync(ct);
        return ApiResponse<CrabLotDto>.Ok(MapLot(lot, placed));
    }

    public async Task<ApiResponse<NextCrabLotCodeDto>> GetNextLotCodeAsync(
        DateTime? importDate = null, CancellationToken ct = default)
    {
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
            Notes = TrimOrNull(req.Notes)
        };
        RecalcDerived(lot);
        await _uow.CrabLots.AddAsync(lot, ct);
        await _uow.SaveChangesAsync(ct);

        var placed = await LoadPlacedCountsAsync(ct);
        return ApiResponse<CrabLotDto>.Ok(MapLot(lot, placed),
            $"Imported {lot.LotCode}. Next: place crabs into boxes.");
    }

    public async Task<ApiResponse<CrabLotDto>> UpdateLotAsync(Guid id, UpdateCrabLotRequest req, CancellationToken ct = default)
    {
        var lot = await _uow.CrabLots.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("CrabLot");

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
        if (req.Status is not null) lot.Status = ParseWorkflowStatus(req.Status, allowCancel: true);

        RecalcDerived(lot);
        _uow.CrabLots.Update(lot);
        await _uow.SaveChangesAsync(ct);

        var placed = await LoadPlacedCountsAsync(ct);
        return ApiResponse<CrabLotDto>.Ok(MapLot(lot, placed));
    }

    public async Task<ApiResponse> DeleteLotAsync(Guid id, CancellationToken ct = default)
    {
        var lot = await _uow.CrabLots.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("CrabLot");

        var inUse = await _uow.Crabs.AnyAsync(c => c.CrabLotId == id, ct);
        if (inUse)
            throw AppException.Conflict(
                $"Cannot delete crab lot '{lot.LotCode}' — crabs still reference it.");

        _uow.CrabLots.Remove(lot);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok($"Crab lot '{lot.LotCode}' deleted.");
    }

    private async Task<Dictionary<Guid, int>> LoadPlacedCountsAsync(CancellationToken ct)
    {
        var crabs = await _uow.Crabs.GetAllAsync(ct);
        return crabs.GroupBy(c => c.CrabLotId).ToDictionary(g => g.Key, g => g.Count());
    }

    private static CrabLotDto MapLot(CrabLot l, IReadOnlyDictionary<Guid, int> placed)
    {
        placed.TryGetValue(l.Id, out var count);
        return new(
            l.Id,
            l.LotCode,
            string.IsNullOrWhiteSpace(l.Name) ? l.LotCode : l.Name,
            l.ImportDate,
            l.Quantity,
            count,
            l.SupplierName,
            l.TotalWeightKg,
            l.AverageWeightGram,
            l.WeightMinGram,
            l.WeightMaxGram,
            l.UnitPriceVndPerKg,
            l.CrabCostVnd,
            l.ShippingCostVnd,
            l.OtherCostVnd,
            l.TotalCostVnd,
            string.IsNullOrWhiteSpace(l.Condition) ? "Good" : l.Condition,
            l.DeadOnArrival,
            l.Notes,
            ResolveWorkflowStatus(l.Status, count, l.Quantity));
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
