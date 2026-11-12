using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3DataIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReleasedAt",
                schema: "be",
                table: "SalesOrderLines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyVersion",
                schema: "be",
                table: "CrabLots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("""
                UPDATE be."SalesOrderLines" line
                SET "ReleasedAt" = COALESCE(line."UpdatedAt", line."CreatedAt", NOW())
                FROM be."SalesOrders" orders
                WHERE orders."Id" = line."SalesOrderId"
                  AND orders."Status" = 'Cancelled'
                  AND line."ReleasedAt" IS NULL;

                ALTER TABLE be."CrabLots"
                  ADD CONSTRAINT "CK_CrabLots_Quantity_Positive"
                  CHECK ("Quantity" > 0) NOT VALID;
                ALTER TABLE be."CrabLots"
                  ADD CONSTRAINT "CK_CrabLots_DeadOnArrival_Range"
                  CHECK ("DeadOnArrival" >= 0 AND "DeadOnArrival" <= "Quantity") NOT VALID;

                CREATE OR REPLACE FUNCTION be.enforce_crab_lot_registration()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    lot_record be."CrabLots"%ROWTYPE;
                    registered_count integer;
                    adding integer;
                BEGIN
                    SELECT * INTO lot_record
                    FROM be."CrabLots"
                    WHERE "Id" = NEW."CrabLotId"
                    FOR UPDATE;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'CrabLot not found';
                    END IF;
                    IF lot_record."ClosedAt" IS NOT NULL THEN
                        RAISE EXCEPTION 'Crab lot is closed';
                    END IF;
                    IF lower(trim(lot_record."Status")) IN ('cancelled', 'canceled') THEN
                        RAISE EXCEPTION 'Crab lot is cancelled';
                    END IF;
                    SELECT COUNT(*) INTO registered_count
                    FROM be."Crabs"
                    WHERE "CrabLotId" = NEW."CrabLotId"
                      AND (TG_OP = 'INSERT' OR "Id" <> NEW."Id");
                    adding := 1;
                    IF registered_count + adding >
                       lot_record."Quantity" - lot_record."DeadOnArrival" THEN
                        RAISE EXCEPTION 'Crab lot capacity exceeded';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_Crabs_EnforceLotRegistration"
                BEFORE INSERT OR UPDATE OF "CrabLotId" ON be."Crabs"
                FOR EACH ROW EXECUTE FUNCTION be.enforce_crab_lot_registration();

                CREATE OR REPLACE FUNCTION be.enforce_crab_lot_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE registered_count integer;
                BEGIN
                    SELECT COUNT(*) INTO registered_count
                    FROM be."Crabs"
                    WHERE "CrabLotId" = NEW."Id";
                    IF NEW."Quantity" < registered_count + NEW."DeadOnArrival" THEN
                        RAISE EXCEPTION
                          'Quantity cannot be lower than registered crabs plus DeadOnArrival';
                    END IF;
                    IF NEW."ClosedAt" IS NOT NULL
                       AND OLD."ClosedAt" IS NULL
                       AND NEW."RegisteredCountAtClose" IS DISTINCT FROM registered_count THEN
                        RAISE EXCEPTION
                          'Close snapshot is stale; retry after concurrent registration';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_CrabLots_EnforceQuantity"
                BEFORE UPDATE OF "Quantity", "DeadOnArrival", "ClosedAt" ON be."CrabLots"
                FOR EACH ROW EXECUTE FUNCTION be.enforce_crab_lot_update();

                CREATE OR REPLACE FUNCTION be.enforce_one_live_crab_per_box()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW."BoxId" IS NULL
                       OR NEW."Status" NOT IN (0, 1, 3) THEN
                        RETURN NEW;
                    END IF;
                    PERFORM 1 FROM be."Boxes"
                    WHERE "Id" = NEW."BoxId"
                    FOR UPDATE;
                    IF EXISTS (
                        SELECT 1 FROM be."Crabs"
                        WHERE "BoxId" = NEW."BoxId"
                          AND "Status" IN (0, 1, 3)
                          AND "Id" <> NEW."Id"
                    ) THEN
                        RAISE EXCEPTION 'Box already contains a live crab';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_Crabs_OneLivePerBox"
                BEFORE INSERT OR UPDATE OF "BoxId", "Status" ON be."Crabs"
                FOR EACH ROW EXECUTE FUNCTION be.enforce_one_live_crab_per_box();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_CrabId",
                schema: "be",
                table: "SalesOrderLines",
                column: "CrabId",
                unique: true,
                filter: "\"CrabId\" IS NOT NULL AND \"ReleasedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_Crabs_OneLivePerBox" ON be."Crabs";
                DROP FUNCTION IF EXISTS be.enforce_one_live_crab_per_box();
                DROP TRIGGER IF EXISTS "TR_CrabLots_EnforceQuantity" ON be."CrabLots";
                DROP FUNCTION IF EXISTS be.enforce_crab_lot_update();
                DROP TRIGGER IF EXISTS "TR_Crabs_EnforceLotRegistration" ON be."Crabs";
                DROP FUNCTION IF EXISTS be.enforce_crab_lot_registration();
                ALTER TABLE be."CrabLots"
                  DROP CONSTRAINT IF EXISTS "CK_CrabLots_Quantity_Positive";
                ALTER TABLE be."CrabLots"
                  DROP CONSTRAINT IF EXISTS "CK_CrabLots_DeadOnArrival_Range";
                """);
            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLines_CrabId",
                schema: "be",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "ReleasedAt",
                schema: "be",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                schema: "be",
                table: "CrabLots");
        }
    }
}
