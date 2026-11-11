using System.Reflection;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Persistence.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class Phase3DataIntegrityMigrationTests
{
    [Fact]
    public void Migration_enforces_lot_capacity_box_exclusivity_and_preserves_history()
    {
        var migration = new Phase3DataIntegrity();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Phase3DataIntegrity)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var sql = string.Join(
            Environment.NewLine,
            builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        sql.Should().Contain("FOR UPDATE");
        sql.Should().Contain("TR_Crabs_EnforceLotRegistration");
        sql.Should().Contain("TR_CrabLots_EnforceQuantity");
        sql.Should().Contain("TR_Crabs_OneLivePerBox");
        sql.Should().Contain("Crab lot capacity exceeded");
        sql.Should().Contain("Box already contains a live crab");
        sql.Should().Contain("Close snapshot is stale");
        sql.Should().Contain("NOT VALID");
        sql.Should().NotContain("DELETE FROM");
        sql.Should().NotContain("DROP TABLE");
    }

    [Fact]
    public void Model_has_lot_concurrency_and_one_active_sales_line_per_crab()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only")
            .Options;
        using var context = new AppDbContext(options);

        var lot = context.Model.FindEntityType(typeof(CrabLot))!;
        lot.FindProperty(nameof(CrabLot.ConcurrencyVersion))!
            .IsConcurrencyToken.Should().BeTrue();

        var line = context.Model.FindEntityType(typeof(SalesOrderLine))!;
        line.GetIndexes().Should().Contain(index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(SalesOrderLine.CrabId) })
            && index.GetFilter()!.Contains(nameof(SalesOrderLine.ReleasedAt)));
    }
}
