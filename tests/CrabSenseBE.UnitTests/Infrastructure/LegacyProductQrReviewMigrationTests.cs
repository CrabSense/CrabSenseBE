using System.Reflection;
using CrabSenseBE.Infrastructure.Persistence.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class LegacyProductQrReviewMigrationTests
{
    [Fact]
    public void Migration_only_demotes_product_qr_without_review_evidence()
    {
        var migration = new ReviewLegacyProductQrApprovals();
        var builder = new MigrationBuilder(
            "Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(ReviewLegacyProductQrApprovals)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var sql = builder.Operations.OfType<SqlOperation>()
            .Should().ContainSingle().Subject.Sql;
        sql.Should().Contain("\"EntityType\" IN ('frozen_lot', 'harvest_voucher')");
        sql.Should().Contain("\"ApprovalStatus\" = 'Approved'");
        sql.Should().Contain("\"ApprovedByUserId\" IS NULL OR \"ApprovedAt\" IS NULL");
        sql.Should().NotContain("\"EntityType\" = 'box'");
        sql.Should().NotContain("DELETE");
        sql.Should().NotContain("SET \"IsActive\"");
        var column = builder.Operations.OfType<AddColumnOperation>()
            .Should().ContainSingle().Subject;
        column.Name.Should().Be("DecisionReason");
        column.Table.Should().Be("QrCodes");
        column.IsNullable.Should().BeTrue();
        builder.Operations.Should().HaveCount(2);
    }
}
