using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>Phiếu nhập lô: tên, khối lượng, giá, tình trạng.</summary>
public partial class CrabLotInbound : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."CrabLots"
            ADD COLUMN IF NOT EXISTS "Name" character varying(128) NOT NULL DEFAULT '',
            ADD COLUMN IF NOT EXISTS "TotalWeightKg" numeric(12,3) NULL,
            ADD COLUMN IF NOT EXISTS "WeightMinGram" numeric(10,2) NULL,
            ADD COLUMN IF NOT EXISTS "WeightMaxGram" numeric(10,2) NULL,
            ADD COLUMN IF NOT EXISTS "UnitPriceVndPerKg" numeric(14,2) NULL,
            ADD COLUMN IF NOT EXISTS "CrabCostVnd" numeric(14,2) NULL,
            ADD COLUMN IF NOT EXISTS "ShippingCostVnd" numeric(14,2) NULL,
            ADD COLUMN IF NOT EXISTS "OtherCostVnd" numeric(14,2) NULL,
            ADD COLUMN IF NOT EXISTS "TotalCostVnd" numeric(14,2) NULL,
            ADD COLUMN IF NOT EXISTS "Condition" character varying(16) NOT NULL DEFAULT 'Good',
            ADD COLUMN IF NOT EXISTS "DeadOnArrival" integer NOT NULL DEFAULT 0;

            UPDATE be."CrabLots"
            SET "Name" = "LotCode"
            WHERE btrim("Name") = '';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."CrabLots"
            DROP COLUMN IF EXISTS "Name",
            DROP COLUMN IF EXISTS "TotalWeightKg",
            DROP COLUMN IF EXISTS "WeightMinGram",
            DROP COLUMN IF EXISTS "WeightMaxGram",
            DROP COLUMN IF EXISTS "UnitPriceVndPerKg",
            DROP COLUMN IF EXISTS "CrabCostVnd",
            DROP COLUMN IF EXISTS "ShippingCostVnd",
            DROP COLUMN IF EXISTS "OtherCostVnd",
            DROP COLUMN IF EXISTS "TotalCostVnd",
            DROP COLUMN IF EXISTS "Condition",
            DROP COLUMN IF EXISTS "DeadOnArrival";
            """);
    }
}
