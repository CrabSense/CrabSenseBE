using CrabSenseBE.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class SyncCursorQueryTests
{
    [Fact]
    public void PostgreSql_provider_translates_stable_timestamp_and_guid_cursor()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only")
            .Options;
        using var context = new AppDbContext(options);
        var timestamp = DateTime.UtcNow;
        var id = Guid.NewGuid();

        var sql = context.Boxes
            .Where(box => (box.UpdatedAt ?? box.CreatedAt) > timestamp
                          || (box.UpdatedAt ?? box.CreatedAt) == timestamp
                          && box.Id.CompareTo(id) > 0)
            .OrderBy(box => box.UpdatedAt ?? box.CreatedAt)
            .ThenBy(box => box.Id)
            .Take(501)
            .ToQueryString();

        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("LIMIT");
    }
}
