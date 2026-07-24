using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class FarmOperationService : IFarmOperationService
{
    private readonly IUnitOfWork _uow;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FarmOperationService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<IEnumerable<FarmOperationDto>>> ListAllAsync(
        int page = 1, int limit = 50, string? type = null,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
    {
        var all = (await _uow.FarmOperations.GetAllAsync(ct)).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(type))
            all = all.Where(o => string.Equals(o.Type, type, StringComparison.OrdinalIgnoreCase));
        if (startDate.HasValue)
            all = all.Where(o => o.Timestamp >= startDate.Value);
        if (endDate.HasValue)
            all = all.Where(o => o.Timestamp <= endDate.Value);

        var pageSize = limit <= 0 ? 50 : limit;
        var pageNum = page <= 0 ? 1 : page;
        var items = all.OrderByDescending(o => o.Timestamp)
            .Skip((pageNum - 1) * pageSize)
            .Take(pageSize)
            .Select(Map)
            .ToList();
        return ApiResponse<IEnumerable<FarmOperationDto>>.Ok(items);
    }

    public async Task<ApiResponse<IEnumerable<FarmOperationDto>>> ListByBoxAsync(
        Guid boxId, int page = 1, int limit = 50, string? type = null,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
    {
        var boxKey = boxId.ToString();
        var all = (await _uow.FarmOperations.GetAllAsync(ct)).AsEnumerable();
        all = all.Where(o => o.BoxIdsJson.Contains(boxKey, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(type))
            all = all.Where(o => string.Equals(o.Type, type, StringComparison.OrdinalIgnoreCase));
        if (startDate.HasValue)
            all = all.Where(o => o.Timestamp >= startDate.Value);
        if (endDate.HasValue)
            all = all.Where(o => o.Timestamp <= endDate.Value);

        var pageSize = limit <= 0 ? 50 : limit;
        var pageNum = page <= 0 ? 1 : page;
        var items = all.OrderByDescending(o => o.Timestamp)
            .Skip((pageNum - 1) * pageSize)
            .Take(pageSize)
            .Select(Map)
            .ToList();
        return ApiResponse<IEnumerable<FarmOperationDto>>.Ok(items);
    }

    public async Task<ApiResponse<FarmOperationDto>> CreateAsync(
        CreateFarmOperationRequest req, CancellationToken ct = default)
    {
        if (req.BoxIds is null || req.BoxIds.Count == 0)
            throw AppException.BadRequest("At least one boxId is required.");
        if (string.IsNullOrWhiteSpace(req.Type))
            throw AppException.BadRequest("Type is required.");

        var op = new FarmOperation
        {
            Type = req.Type.Trim(),
            BoxIdsJson = JsonSerializer.Serialize(req.BoxIds, JsonOpts),
            Quantity = req.Quantity,
            Unit = req.Unit,
            Notes = req.Notes ?? "",
            PhotoUrlsJson = JsonSerializer.Serialize(req.PhotoUrls ?? Array.Empty<string>(), JsonOpts),
            Timestamp = req.Timestamp ?? DateTime.UtcNow,
            OperatorId = req.OperatorId ?? Guid.Empty,
            OperatorName = req.OperatorName ?? "Operator"
        };
        await _uow.FarmOperations.AddAsync(op, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmOperationDto>.Ok(Map(op), "Created.");
    }

    public async Task<ApiResponse<FarmOperationDto>> UpdateAsync(
        Guid id, UpdateFarmOperationRequest req, CancellationToken ct = default)
    {
        var op = await _uow.FarmOperations.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Farm operation");
        if (op.Timestamp < DateTime.UtcNow.AddHours(-24))
            throw AppException.BadRequest("Operation can only be edited within 24 hours.");

        if (!string.IsNullOrWhiteSpace(req.Type)) op.Type = req.Type.Trim();
        if (req.BoxIds is not null) op.BoxIdsJson = JsonSerializer.Serialize(req.BoxIds, JsonOpts);
        if (req.Notes is not null) op.Notes = req.Notes;
        if (req.Quantity.HasValue) op.Quantity = req.Quantity;
        if (req.Unit is not null) op.Unit = req.Unit;
        if (req.PhotoUrls is not null)
            op.PhotoUrlsJson = JsonSerializer.Serialize(req.PhotoUrls, JsonOpts);
        op.UpdatedAt = DateTime.UtcNow;

        _uow.FarmOperations.Update(op);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmOperationDto>.Ok(Map(op), "Updated.");
    }

    private static FarmOperationDto Map(FarmOperation o)
    {
        var boxIds = ParseStringList(o.BoxIdsJson);
        var photos = ParseStringList(o.PhotoUrlsJson);
        return new FarmOperationDto(
            o.Id, o.Type, boxIds, o.Quantity, o.Unit, o.Notes, photos,
            o.Timestamp, o.OperatorId, o.OperatorName);
    }

    private static IReadOnlyList<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
