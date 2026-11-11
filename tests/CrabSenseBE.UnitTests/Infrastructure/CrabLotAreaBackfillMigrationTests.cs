using System.Reflection;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Persistence.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class CrabLotAreaBackfillMigrationTests
{
    [Fact]
    public void Migration_uses_only_verified_box_sources_and_never_updates_business_data()
    {
        var operations = UpOperations();
        var sql = string.Join(
            Environment.NewLine,
            operations.OfType<SqlOperation>().Select(operation => operation.Sql));

        sql.Should().Contain("\"CrabBoxAllocations\"");
        sql.Should().Contain("\"Crabs\"");
        sql.Should().Contain("\"MoltingRecords\"");
        sql.Should().Contain("\"CrabMortalityRecords\"");
        sql.Should().Contain("\"HarvestLines\"");
        sql.Should().Contain("\"Boxes\"");
        sql.Should().Contain("\"FarmingRows\"");
        sql.Should().Contain("ON CONFLICT (\"CrabLotId\", \"FarmingAreaId\") DO NOTHING");
        sql.Should().Contain("\"CrabLotAreaReviewItems\"");
        sql.Should().NotContain("UPDATE \"be\".\"CrabLots\"");
        sql.Should().NotContain("DELETE FROM");
        sql.Should().NotContain("\"StockedAt\"");

        operations.All(operation =>
                operation is CreateTableOperation
                    or CreateIndexOperation
                    or SqlOperation)
            .Should().BeTrue();
    }

    [Fact]
    public void Model_keeps_unique_lot_area_link_and_unique_unresolved_review()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new AppDbContext(options);

        var lotArea = context.Model.FindEntityType(
            "CrabSenseBE.Domain.Entities.CrabLotArea")!;
        var linkIndex = lotArea.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "CrabLotId", "FarmingAreaId" }));
        linkIndex.IsUnique.Should().BeTrue();

        var review = context.Model.FindEntityType(
            "CrabSenseBE.Domain.Entities.CrabLotAreaReviewItem")!;
        review.GetIndexes().Single(index =>
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(new[] { "CrabLotId" }))
            .IsUnique.Should().BeTrue();
    }

    private static IReadOnlyList<MigrationOperation> UpOperations()
    {
        var migration = new BackfillHistoricalCrabLotAreas();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(BackfillHistoricalCrabLotAreas)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, new object[] { builder });
        return builder.Operations;
    }
}
