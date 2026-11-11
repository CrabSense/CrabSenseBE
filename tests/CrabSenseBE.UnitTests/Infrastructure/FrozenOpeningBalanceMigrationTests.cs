using System.Reflection;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Persistence.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class FrozenOpeningBalanceMigrationTests
{
    [Fact]
    public void Migration_reconstructs_once_without_updating_inventory()
    {
        var migration = new AddFrozenInventoryOpeningBalances();
        var builder = new MigrationBuilder(
            "Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddFrozenInventoryOpeningBalances)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var sql = string.Join(
            Environment.NewLine,
            builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        sql.Should().Contain("LOCK TABLE be.\"FrozenLots\"");
        sql.Should().Contain("lot.\"Quantity\" - COALESCE(movements.quantity_delta, 0)");
        sql.Should().Contain("lot.\"WeightKg\" - COALESCE(movements.weight_delta, 0)");
        sql.Should().Contain("ON CONFLICT (\"FrozenLotId\") DO NOTHING");
        sql.Should().Contain("THEN 'NeedsReview'");
        sql.Should().Contain("prevent_verified_frozen_opening_balance_change");
        sql.Should().NotContain("UPDATE be.\"FrozenLots\"");
        sql.Should().NotContain("DELETE FROM be.\"FrozenLots\"");
    }

    [Fact]
    public void Model_has_one_opening_balance_per_frozen_lot()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only")
            .Options;
        using var context = new AppDbContext(options);
        var entity = context.Model.FindEntityType(
            typeof(FrozenInventoryOpeningBalance))!;

        entity.GetIndexes().Should().ContainSingle(index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[]
                {
                    nameof(FrozenInventoryOpeningBalance.FrozenLotId)
                }));
    }
}
