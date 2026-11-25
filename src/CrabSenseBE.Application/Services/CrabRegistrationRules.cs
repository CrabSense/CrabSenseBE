using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

internal static class CrabRegistrationRules
{
    internal static void ValidateMeasurements(decimal? weight, decimal? width, decimal? length)
    {
        if (weight is null or <= 0)
            throw AppException.BadRequest("WeightGram is required and must be > 0.");
        if (width is null or <= 0)
            throw AppException.BadRequest("CarapaceWidthMm (bề rộng mai) is required and must be > 0.");
        if (length is null or <= 0)
            throw AppException.BadRequest("CarapaceLengthMm (bề ngang mai) is required and must be > 0.");
    }

    internal static bool IsEmptyBox(Box box) =>
        !box.IsOccupied && string.Equals(box.Status?.Trim(), BoxStatuses.Empty, StringComparison.OrdinalIgnoreCase);

    internal static async Task<bool> HasOccupancyAsync(IUnitOfWork uow, Guid boxId, CancellationToken ct) =>
        await uow.CrabBoxAllocations.AnyAsync(a => a.BoxId == boxId && a.EndTime == null, ct)
        || await uow.Crabs.AnyAsync(c => c.BoxId == boxId &&
            (c.Status == CrabStatus.Alive || c.Status == CrabStatus.Molting || c.Status == CrabStatus.Quarantined), ct);

    internal static async Task EnsureAvailableAsync(IUnitOfWork uow, Box box, CancellationToken ct)
    {
        if (!IsEmptyBox(box) || await HasOccupancyAsync(uow, box.Id, ct))
            throw AppException.Conflict($"Hộp '{box.Code}' không trống hoặc vừa được sử dụng.");
    }

    internal static bool IsBoxUnusable(string? status) =>
        (status ?? "").Trim().ToLowerInvariant() is
            "maintenance" or "quarantine" or "harvested" or "suspended" or "closed";

    internal static async Task AddInitialHistoryAsync(
        IUnitOfWork uow, FarmAreaScope areas, Crab crab, string source, CancellationToken ct)
    {
        var actor = await areas.CurrentUserAsync(ct);
        await uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
        {
            CrabId = crab.Id,
            NewCondition = crab.Condition,
            NewStatus = crab.Status,
            ChangedAt = DateTime.UtcNow,
            ChangedByUserId = actor?.Id,
            Source = source,
            Reason = "CRAB_CREATED"
        }, ct);
        if (crab.InitialWeightGram is decimal weight)
        {
            await uow.CrabWeightHistories.AddAsync(new CrabWeightHistory
            {
                CrabId = crab.Id,
                WeightGram = weight,
                CarapaceWidthMm = crab.CarapaceWidthMm,
                CarapaceLengthMm = crab.CarapaceLengthMm,
                MeasuredAt = crab.StockedAt,
                Source = "INITIAL",
                Notes = "Đo lường ban đầu",
                RecordedByName = actor is null ? null :
                    string.IsNullOrWhiteSpace(actor.FullName) ? actor.Username : actor.FullName
            }, ct);
        }
    }
}
