using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public static class CrabLotRegistrationGuard
{
    public static async Task<CrabLot> EnsureCanRegisterAsync(
        IUnitOfWork uow,
        Guid lotId,
        int adding,
        CancellationToken ct)
    {
        var lot = await uow.CrabLots.GetByIdAsync(lotId, ct)
            ?? throw AppException.NotFound("CrabLot");
        if (lot.ClosedAt is not null)
            throw AppException.Conflict("Lô cua đã kết thúc, không thể đăng ký thêm cua.");
        if (string.Equals(lot.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest("Lô nhập đã hủy.");

        var placed = await uow.Crabs.CountAsync(c => c.CrabLotId == lotId, ct);
        var remaining = lot.Quantity - placed - lot.DeadOnArrival;
        if (remaining < adding)
            throw AppException.BadRequest(
                $"Lô này chỉ còn {Math.Max(0, remaining)} cá thể chưa được tạo.");
        return lot;
    }

    public static async Task<Guid> EnsureLotAllowsBoxAsync(
        IUnitOfWork uow,
        Guid lotId,
        Guid boxId,
        CancellationToken ct)
    {
        var box = await uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");
        var row = await uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct)
            ?? throw AppException.NotFound("FarmingRow");
        await EnsureLotAllowsAreaAsync(uow, lotId, row.FarmingAreaId, ct);
        return row.FarmingAreaId;
    }

    public static async Task EnsureLotAllowsAreaAsync(
        IUnitOfWork uow,
        Guid lotId,
        Guid farmingAreaId,
        CancellationToken ct)
    {
        var allowed = await uow.CrabLotAreas.AnyAsync(
            link => link.CrabLotId == lotId
                && link.FarmingAreaId == farmingAreaId,
            ct);
        if (!allowed)
        {
            throw AppException.Forbidden(
                "Crab Lot không được phân bổ vào khu của hộp này.");
        }
    }
}
