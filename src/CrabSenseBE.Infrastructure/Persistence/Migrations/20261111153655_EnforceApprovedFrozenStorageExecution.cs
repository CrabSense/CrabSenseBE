using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceApprovedFrozenStorageExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovedQuantity",
                schema: "be",
                table: "FrozenStorageTransactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedWeightKg",
                schema: "be",
                table: "FrozenStorageTransactions",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyVersion",
                schema: "be",
                table: "FrozenStorageTransactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "QuantityVariance",
                schema: "be",
                table: "FrozenStorageTransactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VarianceReason",
                schema: "be",
                table: "FrozenStorageTransactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightVarianceKg",
                schema: "be",
                table: "FrozenStorageTransactions",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyVersion",
                schema: "be",
                table: "FrozenLots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                """
                UPDATE be."FrozenStorageTransactions"
                SET "ApprovedQuantity" = "RequestedQuantity",
                    "ApprovedWeightKg" = "RequestedWeightKg"
                WHERE "Status" IN ('Approved', 'Completed');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedQuantity",
                schema: "be",
                table: "FrozenStorageTransactions");

            migrationBuilder.DropColumn(
                name: "ApprovedWeightKg",
                schema: "be",
                table: "FrozenStorageTransactions");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                schema: "be",
                table: "FrozenStorageTransactions");

            migrationBuilder.DropColumn(
                name: "QuantityVariance",
                schema: "be",
                table: "FrozenStorageTransactions");

            migrationBuilder.DropColumn(
                name: "VarianceReason",
                schema: "be",
                table: "FrozenStorageTransactions");

            migrationBuilder.DropColumn(
                name: "WeightVarianceKg",
                schema: "be",
                table: "FrozenStorageTransactions");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                schema: "be",
                table: "FrozenLots");
        }
    }
}
