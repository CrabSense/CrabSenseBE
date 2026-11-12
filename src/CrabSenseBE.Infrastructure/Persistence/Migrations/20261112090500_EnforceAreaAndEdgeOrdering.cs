using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    public partial class EnforceAreaAndEdgeOrdering : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refuse ambiguous legacy assignments instead of silently reassigning staff.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM be."FarmStaffAssignments" s
                        LEFT JOIN be."FarmManagerAssignments" m
                          ON m."ManagerUserId" = s."ManagerUserId"
                         AND m."FarmingAreaId" = s."FarmingAreaId"
                        WHERE m."Id" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Legacy staff assignments lack matching manager-area assignment; review before migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(name: "IX_FarmManagerAssignments_ManagerUserId_FarmingAreaId",
                schema: "be", table: "FarmManagerAssignments");
            migrationBuilder.AddUniqueConstraint(name: "AK_FarmManagerAssignments_ManagerUserId_FarmingAreaId",
                schema: "be", table: "FarmManagerAssignments", columns: new[] { "ManagerUserId", "FarmingAreaId" });
            migrationBuilder.CreateIndex(name: "IX_FarmStaffAssignments_ManagerUserId_FarmingAreaId",
                schema: "be", table: "FarmStaffAssignments", columns: new[] { "ManagerUserId", "FarmingAreaId" });
            migrationBuilder.AddForeignKey(name: "FK_StaffAssignment_ManagerArea",
                schema: "be", table: "FarmStaffAssignments", columns: new[] { "ManagerUserId", "FarmingAreaId" },
                principalSchema: "be", principalTable: "FarmManagerAssignments",
                principalColumns: new[] { "ManagerUserId", "FarmingAreaId" }, onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddColumn<Guid>(name: "FarmingAreaId", schema: "be", table: "NotificationRules",
                type: "uuid", nullable: true);
            migrationBuilder.DropIndex(name: "IX_NotificationRules_TypeCode", schema: "be", table: "NotificationRules");
            migrationBuilder.CreateIndex(name: "IX_NotificationRules_TypeCode", schema: "be", table: "NotificationRules",
                column: "TypeCode", unique: true, filter: "\"FarmingAreaId\" IS NULL");
            migrationBuilder.CreateIndex(name: "IX_NotificationRules_FarmingAreaId_TypeCode", schema: "be",
                table: "NotificationRules", columns: new[] { "FarmingAreaId", "TypeCode" }, unique: true,
                filter: "\"FarmingAreaId\" IS NOT NULL");
            migrationBuilder.AddForeignKey(name: "FK_NotificationRules_FarmingAreas_FarmingAreaId", schema: "be",
                table: "NotificationRules", column: "FarmingAreaId", principalSchema: "be",
                principalTable: "FarmingAreas", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddColumn<long>(name: "CommandSequence", schema: "be", table: "EdgeCommands",
                type: "bigint", nullable: false, defaultValue: 0L);
            migrationBuilder.AddColumn<DateTime>(name: "SupersededAt", schema: "be", table: "EdgeCommands",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "SupersededByCommandId", schema: "be", table: "EdgeCommands",
                type: "uuid", nullable: true);
            migrationBuilder.Sql("""
                CREATE SEQUENCE be.edge_command_sequence AS bigint;
                WITH ordered AS (
                    SELECT "Id", row_number() OVER (ORDER BY "CreatedAt", "Id") AS seq
                    FROM be."EdgeCommands"
                )
                UPDATE be."EdgeCommands" c SET "CommandSequence" = ordered.seq
                FROM ordered WHERE c."Id" = ordered."Id";
                SELECT setval('be.edge_command_sequence',
                    COALESCE((SELECT max("CommandSequence") FROM be."EdgeCommands"), 0) + 1, false);

                -- Legacy in-flight commands have no firmware ordering guarantee.
                -- Preserve the audit record but never replay them after protocol cutover.
                UPDATE be."EdgeCommands"
                SET "Status" = 'superseded', "SupersededAt" = now(),
                    "ResultMessage" = 'Protocol 2 cutover: reissue desired state after firmware upgrade.',
                    "LeaseExpiresAt" = NULL, "DeliveryAttemptId" = NULL,
                    "ConcurrencyVersion" = "ConcurrencyVersion" + 1
                WHERE "Status" IN ('pending', 'delivered');

                CREATE FUNCTION be.assign_edge_command_sequence() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    -- Serialize command inserts for the same device. Sequence allocation
                    -- and invalidation of prior leases commit with the new command.
                    PERFORM pg_advisory_xact_lock(hashtextextended(NEW."DeviceCode", 0));
                    NEW."CommandSequence" := nextval('be.edge_command_sequence');
                    UPDATE be."EdgeCommands" c
                    SET "Status" = 'superseded', "SupersededAt" = now(),
                        "SupersededByCommandId" = NEW."Id",
                        "ResultMessage" = 'Superseded by a newer command.',
                        "LeaseExpiresAt" = NULL, "DeliveryAttemptId" = NULL,
                        "ConcurrencyVersion" = c."ConcurrencyVersion" + 1
                    WHERE c."DeviceCode" = NEW."DeviceCode"
                      AND c."Status" IN ('pending', 'delivered')
                      AND (NEW."Command" = 'alloff'
                           OR (c."Command" <> 'alloff' AND c."Channel" = NEW."Channel"));
                    RETURN NEW;
                END $$;
                CREATE TRIGGER trg_edge_command_sequence
                    BEFORE INSERT ON be."EdgeCommands"
                    FOR EACH ROW EXECUTE FUNCTION be.assign_edge_command_sequence();
                """);
            migrationBuilder.CreateIndex(name: "IX_EdgeCommands_CommandSequence", schema: "be", table: "EdgeCommands",
                column: "CommandSequence", unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Downgrade is intentionally blocked if area overrides or ordered commands
            // exist: deleting these would discard policy or allow unsafe command replay.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM be."NotificationRules" WHERE "FarmingAreaId" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM be."EdgeCommands" WHERE "CommandSequence" > 0)
                    THEN
                        RAISE EXCEPTION 'Cannot downgrade area policies or ordered edge history; restore a verified backup instead.';
                    END IF;
                END $$;
                DROP TRIGGER trg_edge_command_sequence ON be."EdgeCommands";
                DROP FUNCTION be.assign_edge_command_sequence();
                DROP SEQUENCE be.edge_command_sequence;
                """);
            migrationBuilder.DropIndex(name: "IX_EdgeCommands_CommandSequence", schema: "be", table: "EdgeCommands");
            migrationBuilder.DropColumn(name: "SupersededByCommandId", schema: "be", table: "EdgeCommands");
            migrationBuilder.DropColumn(name: "SupersededAt", schema: "be", table: "EdgeCommands");
            migrationBuilder.DropColumn(name: "CommandSequence", schema: "be", table: "EdgeCommands");
            migrationBuilder.DropForeignKey(name: "FK_NotificationRules_FarmingAreas_FarmingAreaId", schema: "be", table: "NotificationRules");
            migrationBuilder.DropIndex(name: "IX_NotificationRules_FarmingAreaId_TypeCode", schema: "be", table: "NotificationRules");
            migrationBuilder.DropIndex(name: "IX_NotificationRules_TypeCode", schema: "be", table: "NotificationRules");
            migrationBuilder.DropColumn(name: "FarmingAreaId", schema: "be", table: "NotificationRules");
            migrationBuilder.CreateIndex(name: "IX_NotificationRules_TypeCode", schema: "be", table: "NotificationRules", column: "TypeCode", unique: true);
            migrationBuilder.DropForeignKey(name: "FK_StaffAssignment_ManagerArea", schema: "be", table: "FarmStaffAssignments");
            migrationBuilder.DropIndex(name: "IX_FarmStaffAssignments_ManagerUserId_FarmingAreaId", schema: "be", table: "FarmStaffAssignments");
            migrationBuilder.DropUniqueConstraint(name: "AK_FarmManagerAssignments_ManagerUserId_FarmingAreaId", schema: "be", table: "FarmManagerAssignments");
            migrationBuilder.CreateIndex(name: "IX_FarmManagerAssignments_ManagerUserId_FarmingAreaId", schema: "be",
                table: "FarmManagerAssignments", columns: new[] { "ManagerUserId", "FarmingAreaId" }, unique: true);
        }
    }
}
