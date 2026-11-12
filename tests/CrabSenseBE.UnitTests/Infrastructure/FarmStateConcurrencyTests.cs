using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class FarmStateConcurrencyTests
{
    [Theory]
    [InlineData(typeof(Box))]
    [InlineData(typeof(Crab))]
    public void Farm_state_version_is_database_generated_concurrency_token(Type entity)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        var property = db.Model.FindEntityType(entity)!.FindProperty("ConcurrencyVersion")!;
        Assert.True(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.Equal(0L, property.GetDefaultValue());
    }
}
