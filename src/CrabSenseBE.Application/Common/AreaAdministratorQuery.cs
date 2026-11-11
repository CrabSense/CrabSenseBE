using CrabSenseBE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace CrabSenseBE.Application.Common;

/// <summary>Đọc Farm Administrator của khu từ bảng gán. Hỗ trợ cả EF và LINQ trong unit test.</summary>
internal static class AreaAdministratorQuery
{
    public readonly record struct Person(Guid Id, string? Name);

    public static async Task<Dictionary<Guid, Person>> ByAreaAsync(
        IQueryable<FarmingArea>? areas, IEnumerable<Guid> areaIds, CancellationToken ct)
    {
        var ids = areaIds.Where(id => id != Guid.Empty).ToHashSet();
        if (areas is null || ids.Count == 0)
            return new Dictionary<Guid, Person>();

        var rows = await MaterializeAsync(
            areas.Where(area => ids.Contains(area.Id))
                .SelectMany(
                    area => area.Administrators,
                    (area, admin) => new { AreaId = area.Id, admin.Id, admin.FullName }),
            ct);
        return rows
            .GroupBy(row => row.AreaId)
            .ToDictionary(group => group.Key, group => new Person(group.First().Id, group.First().FullName));
    }

    public static async Task<Guid> IdAsync(
        IQueryable<FarmingArea>? areas, Guid areaId, CancellationToken ct)
    {
        var people = await ByAreaAsync(areas, new[] { areaId }, ct);
        return people.TryGetValue(areaId, out var person) ? person.Id : Guid.Empty;
    }

    private static async Task<List<T>> MaterializeAsync<T>(IQueryable<T> query, CancellationToken ct)
    {
        if (query.Provider is IAsyncQueryProvider)
            return await query.ToListAsync(ct);
        return query.ToList();
    }
}
