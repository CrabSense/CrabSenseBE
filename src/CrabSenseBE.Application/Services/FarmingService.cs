using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class FarmingService : IFarmingService
{
    private readonly IUnitOfWork _uow;

    public FarmingService(IUnitOfWork uow) => _uow = uow;

    // ─── FarmingArea ────────────────────────────────────────────────────────
    public async Task<ApiResponse<IEnumerable<FarmingAreaDto>>> GetAreasAsync(CancellationToken ct = default)
    {
        var areas = await _uow.FarmingAreas.GetAllAsync(ct);
        return ApiResponse<IEnumerable<FarmingAreaDto>>.Ok(areas.Select(MapArea));
    }

    public async Task<ApiResponse<FarmingAreaDto>> GetAreaByIdAsync(Guid id, CancellationToken ct = default)
    {
        var area = await _uow.FarmingAreas.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("FarmingArea");
        return ApiResponse<FarmingAreaDto>.Ok(MapArea(area));
    }

    public async Task<ApiResponse<FarmingAreaDto>> CreateAreaAsync(CreateFarmingAreaRequest req, CancellationToken ct = default)
    {
        var area = new FarmingArea { Name = req.Name, Description = req.Description };
        await _uow.FarmingAreas.AddAsync(area, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmingAreaDto>.Ok(MapArea(area), "Created.");
    }

    public async Task<ApiResponse<FarmingAreaDto>> UpdateAreaAsync(Guid id, UpdateFarmingAreaRequest req, CancellationToken ct = default)
    {
        var area = await _uow.FarmingAreas.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("FarmingArea");
        area.Name = req.Name;
        area.Description = req.Description;
        area.IsActive = req.IsActive;
        _uow.FarmingAreas.Update(area);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmingAreaDto>.Ok(MapArea(area));
    }

    public async Task<ApiResponse> DeleteAreaAsync(Guid id, CancellationToken ct = default)
    {
        var area = await _uow.FarmingAreas.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("FarmingArea");
        _uow.FarmingAreas.Remove(area);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Deleted.");
    }

    // ─── FarmingRow ─────────────────────────────────────────────────────────
    public async Task<ApiResponse<IEnumerable<FarmingRowDto>>> GetRowsByAreaAsync(Guid areaId, CancellationToken ct = default)
    {
        var rows = await _uow.FarmingRows.FindAsync(r => r.FarmingAreaId == areaId, ct);
        return ApiResponse<IEnumerable<FarmingRowDto>>.Ok(rows.Select(MapRow));
    }

    public async Task<ApiResponse<FarmingRowDto>> CreateRowAsync(CreateFarmingRowRequest req, CancellationToken ct = default)
    {
        var row = new FarmingRow { FarmingAreaId = req.FarmingAreaId, Name = req.Name, Capacity = req.Capacity };
        await _uow.FarmingRows.AddAsync(row, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmingRowDto>.Ok(MapRow(row), "Created.");
    }

    // ─── Box ────────────────────────────────────────────────────────────────
    public async Task<ApiResponse<IEnumerable<BoxDto>>> GetBoxesByRowAsync(Guid rowId, CancellationToken ct = default)
    {
        var boxes = await _uow.Boxes.FindAsync(b => b.FarmingRowId == rowId, ct);
        return ApiResponse<IEnumerable<BoxDto>>.Ok(boxes.Select(MapBox));
    }

    public async Task<ApiResponse<BoxDto>> CreateBoxAsync(CreateBoxRequest req, CancellationToken ct = default)
    {
        var box = new Box { FarmingRowId = req.FarmingRowId, Code = req.Code };
        await _uow.Boxes.AddAsync(box, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<BoxDto>.Ok(MapBox(box), "Created.");
    }

    // ─── Crab ───────────────────────────────────────────────────────────────
    public async Task<ApiResponse<PagedResult<CrabDto>>> GetCrabsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var all = await _uow.Crabs.GetAllAsync(ct);
        var total = all.Count();
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).Select(MapCrab);
        return ApiResponse<PagedResult<CrabDto>>.Ok(new PagedResult<CrabDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ApiResponse<CrabDto>> GetCrabByIdAsync(Guid id, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Crab");
        return ApiResponse<CrabDto>.Ok(MapCrab(crab));
    }

    public async Task<ApiResponse<CrabDto>> CreateCrabAsync(CreateCrabRequest req, CancellationToken ct = default)
    {
        var crab = new Crab { BoxId = req.BoxId, CrabLotId = req.CrabLotId, Tag = req.Tag, WeightGram = req.WeightGram };
        await _uow.Crabs.AddAsync(crab, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<CrabDto>.Ok(MapCrab(crab), "Created.");
    }

    public async Task<ApiResponse<CrabDto>> UpdateCrabAsync(Guid id, UpdateCrabRequest req, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Crab");
        crab.MoltingStage = req.MoltingStage;
        crab.WeightGram = req.WeightGram;
        crab.IsAlive = req.IsAlive;
        crab.MoltedAt = req.MoltedAt;
        _uow.Crabs.Update(crab);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<CrabDto>.Ok(MapCrab(crab));
    }

    // ─── Mappers ────────────────────────────────────────────────────────────
    private static FarmingAreaDto MapArea(FarmingArea a) =>
        new(a.Id, a.Name, a.Description, a.IsActive, a.Rows?.Count ?? 0);

    private static FarmingRowDto MapRow(FarmingRow r) =>
        new(r.Id, r.FarmingAreaId, r.Name, r.Capacity, r.IsActive, r.Boxes?.Count ?? 0);

    private static BoxDto MapBox(Box b) =>
        new(b.Id, b.FarmingRowId, b.Code, b.Status, b.IsOccupied);

    private static CrabDto MapCrab(Crab c) =>
        new(c.Id, c.BoxId, c.CrabLotId, c.Tag, c.WeightGram, c.MoltingStage, c.IsAlive, c.MoltedAt);
}
