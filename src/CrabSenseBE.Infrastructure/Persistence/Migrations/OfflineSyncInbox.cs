using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

internal static class OfflineSyncInboxLegacySql
{
    public static void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS be."SyncInboxItems" (
                "Id" uuid NOT NULL,
                "IdempotencyKey" character varying(128) NOT NULL,
                "EntityType" character varying(64) NOT NULL,
                "EntityId" character varying(128) NOT NULL,
                "OperationType" character varying(128) NOT NULL,
                "BaseVersion" integer NULL,
                "ClientUpdatedAt" timestamp with time zone NULL,
                "PayloadJson" jsonb NOT NULL,
                "Status" character varying(32) NOT NULL,
                "ErrorMessage" text NULL,
                "ReceivedAt" timestamp with time zone NOT NULL,
                "ProcessedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_SyncInboxItems" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SyncInboxItems_IdempotencyKey"
                ON be."SyncInboxItems" ("IdempotencyKey");
            CREATE INDEX IF NOT EXISTS "IX_SyncInboxItems_Status_ReceivedAt"
                ON be."SyncInboxItems" ("Status", "ReceivedAt");
            """);
    }

    public static void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""DROP TABLE IF EXISTS be."SyncInboxItems";""");
}
