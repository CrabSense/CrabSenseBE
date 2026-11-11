using System.Reflection;
using CrabSenseBE.Infrastructure.Persistence.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class UserTokenVersionMigrationTests
{
    [Fact]
    public void Migration_adds_token_version_without_deleting_users()
    {
        var migration = new AddUserTokenVersion();
        var builder = new MigrationBuilder(
            "Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddUserTokenVersion)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var column = builder.Operations.OfType<AddColumnOperation>()
            .Should().ContainSingle().Subject;
        column.Table.Should().Be("AppUsers");
        column.Name.Should().Be("TokenVersion");
        column.IsNullable.Should().BeFalse();
        column.DefaultValue.Should().Be(0);
        builder.Operations.OfType<DeleteDataOperation>().Should().BeEmpty();
        builder.Operations.OfType<SqlOperation>().Should().BeEmpty();
    }
}
