using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    public partial class RepairMissingFrozenCrabItems : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The preceding AddFrozenCrabItems migration normally owns this table.
            // Repair a genuinely missing legacy table without recreating or deleting
            // an existing table or its data.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS be."FrozenCrabItems" (
                    "Id" uuid NOT NULL,
                    "FrozenLotId" uuid NOT NULL,
                    "HarvestLineId" uuid NOT NULL,
                    "CrabId" uuid NOT NULL,
                    "BarcodeValue" character varying(40) NOT NULL,
                    "LotCode" character varying(64) NOT NULL,
                    "CrabCode" character varying(64) NOT NULL,
                    "HarvestVoucherCode" character varying(64) NULL,
                    "HarvestDate" timestamp with time zone NOT NULL,
                    "FrozenDate" timestamp with time zone NOT NULL,
                    "ExpiryDate" timestamp with time zone NOT NULL,
                    "WeightGram" numeric(10,2) NOT NULL,
                    "Grade" character varying(1) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_FrozenCrabItems" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_FrozenCrabItems_Crabs_CrabId"
                        FOREIGN KEY ("CrabId") REFERENCES be."Crabs" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_FrozenCrabItems_FrozenLots_FrozenLotId"
                        FOREIGN KEY ("FrozenLotId") REFERENCES be."FrozenLots" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_FrozenCrabItems_HarvestLines_HarvestLineId"
                        FOREIGN KEY ("HarvestLineId") REFERENCES be."HarvestLines" ("Id") ON DELETE RESTRICT
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_FrozenCrabItems_BarcodeValue"
                    ON be."FrozenCrabItems" ("BarcodeValue");
                CREATE INDEX IF NOT EXISTS "IX_FrozenCrabItems_CrabId"
                    ON be."FrozenCrabItems" ("CrabId");
                CREATE INDEX IF NOT EXISTS "IX_FrozenCrabItems_FrozenLotId"
                    ON be."FrozenCrabItems" ("FrozenLotId");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_FrozenCrabItems_HarvestLineId"
                    ON be."FrozenCrabItems" ("HarvestLineId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous migration owns FrozenCrabItems. A rollback of the repair
            // must not remove the table or its inventory records.
        }
    }
}
