using System.Reflection;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Persistence.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class FarmManagerMultiAreaMigrationTests
{
    [Fact]
    public void Migration_replaces_only_the_manager_unique_index()
    {
        var operations = UpOperations();

        operations.Should().HaveCount(2);
        operations.OfType<DropIndexOperation>()
            .Should().ContainSingle(drop =>
                drop.Name == "IX_FarmManagerAssignments_ManagerUserId");
        var create = operations.OfType<CreateIndexOperation>().Single();
        create.Name.Should().Be(
            "IX_FarmManagerAssignments_ManagerUserId_FarmingAreaId");
        create.Columns.Should().Equal("ManagerUserId", "FarmingAreaId");
        create.IsUnique.Should().BeTrue();
        operations.OfType<DeleteDataOperation>().Should().BeEmpty();
        operations.OfType<UpdateDataOperation>().Should().BeEmpty();
        operations.OfType<SqlOperation>().Should().BeEmpty();
    }

    [Fact]
    public void Model_allows_many_areas_but_rejects_duplicate_manager_area_pair()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new AppDbContext(options);
        var entity = context.Model.FindEntityType(typeof(FarmManagerAssignment))!;

        entity.GetIndexes().Any(index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "ManagerUserId", "FarmingAreaId" }))
            .Should().BeTrue();
        entity.GetIndexes().Any(index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "ManagerUserId" }))
            .Should().BeFalse();
        entity.GetIndexes().Any(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "FarmingAreaId" }))
            .Should().BeTrue();
    }

    private static IReadOnlyList<MigrationOperation> UpOperations()
    {
        var migration = new EnableMultiAreaFarmManagers();
        var builder = new MigrationBuilder(
            "Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(EnableMultiAreaFarmManagers)
            .GetMethod(
                "Up",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }
}
