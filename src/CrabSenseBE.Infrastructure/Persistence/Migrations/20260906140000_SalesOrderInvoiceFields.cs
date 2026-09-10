using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>Additive sales invoice fields: totals, payment method, delivery, line type/weight.</summary>
public partial class SalesOrderInvoiceFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE be."SalesOrders"
            ADD COLUMN IF NOT EXISTS "SubtotalAmount" numeric NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "DiscountAmount" numeric NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "ShippingFee" numeric NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "PaidAmount" numeric NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "PaymentMethod" text NULL,
            ADD COLUMN IF NOT EXISTS "DeliveryStatus" text NULL;

            ALTER TABLE be."SalesOrderLines"
            ADD COLUMN IF NOT EXISTS "CrabType" text NULL,
            ADD COLUMN IF NOT EXISTS "WeightGram" numeric NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep additive columns.
    }
}
