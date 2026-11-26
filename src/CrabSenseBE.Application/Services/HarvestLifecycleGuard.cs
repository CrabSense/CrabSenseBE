using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

internal static class HarvestLifecycleGuard
{
    internal static async Task<bool> HasOpenReservationAsync(IUnitOfWork uow, HashSet<Guid> crabIds, CancellationToken ct)
    {
        if (crabIds.Count == 0)
            return false;

        if (await uow.HarvestAssignmentLines.AnyAsync(line => crabIds.Contains(line.CrabId)
            && (line.Status == HarvestAssignmentLineStatus.Assigned
                || line.Status == HarvestAssignmentLineStatus.Eligible
                || line.Status == HarvestAssignmentLineStatus.InProgress), ct))
            return true;

        var voucherIds = (await uow.HarvestLines.FindAsync(line =>
                line.CrabId.HasValue && crabIds.Contains(line.CrabId.Value), ct))
            .Select(line => line.HarvestVoucherId).ToHashSet();
        return voucherIds.Count > 0 && await uow.HarvestVouchers.AnyAsync(v =>
            voucherIds.Contains(v.Id)
            && (v.Status == HarvestStatus.PendingApproval || v.Status == HarvestStatus.Approved
                || v.Status == HarvestStatus.Planned || v.Status == HarvestStatus.InProgress), ct);
    }

    internal static Task<bool> HasPendingAssignmentEvidenceAsync(IUnitOfWork uow, Guid crabId, CancellationToken ct)
        => uow.HarvestAssignmentLines.AnyAsync(line => line.CrabId == crabId
            && (line.Status == HarvestAssignmentLineStatus.Assigned
                || line.Status == HarvestAssignmentLineStatus.Eligible
                || line.Status == HarvestAssignmentLineStatus.InProgress)
            && (line.Outcome == HarvestAssignmentOutcome.Harvested
                || line.PendingMoltConfirmed || line.PendingHarvestedAt.HasValue), ct);
}
