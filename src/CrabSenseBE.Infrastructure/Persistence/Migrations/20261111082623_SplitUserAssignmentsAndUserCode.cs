using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitUserAssignmentsAndUserCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppUsers_FarmingAreas_FarmingAreaId",
                schema: "be",
                table: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_EmployeeId",
                schema: "be",
                table: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_FarmingAreaId",
                schema: "be",
                table: "AppUsers");

            migrationBuilder.DropSequence(
                name: "employee_id_sequence",
                schema: "be");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                schema: "be",
                table: "AppUsers",
                newName: "UserCode");

            migrationBuilder.CreateSequence(
                name: "user_code_fad_seq",
                schema: "be");

            migrationBuilder.CreateSequence(
                name: "user_code_fmn_seq",
                schema: "be");

            migrationBuilder.CreateSequence(
                name: "user_code_fst_seq",
                schema: "be");

            migrationBuilder.CreateSequence(
                name: "user_code_sad_seq",
                schema: "be");

            migrationBuilder.CreateTable(
                name: "FarmManagerAssignments",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FarmManagerAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FarmManagerAssignments_AppUsers_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalSchema: "be",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FarmManagerAssignments_AppUsers_ManagerUserId",
                        column: x => x.ManagerUserId,
                        principalSchema: "be",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FarmManagerAssignments_FarmingAreas_FarmingAreaId",
                        column: x => x.FarmingAreaId,
                        principalSchema: "be",
                        principalTable: "FarmingAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FarmStaffAssignments",
                schema: "be",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FarmingAreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FarmStaffAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FarmStaffAssignments_AppUsers_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalSchema: "be",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FarmStaffAssignments_AppUsers_ManagerUserId",
                        column: x => x.ManagerUserId,
                        principalSchema: "be",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FarmStaffAssignments_AppUsers_StaffUserId",
                        column: x => x.StaffUserId,
                        principalSchema: "be",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FarmStaffAssignments_FarmingAreas_FarmingAreaId",
                        column: x => x.FarmingAreaId,
                        principalSchema: "be",
                        principalTable: "FarmingAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_UserCode",
                schema: "be",
                table: "AppUsers",
                column: "UserCode",
                unique: true,
                filter: "\"UserCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FarmManagerAssignments_AssignedByUserId",
                schema: "be",
                table: "FarmManagerAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FarmManagerAssignments_FarmingAreaId",
                schema: "be",
                table: "FarmManagerAssignments",
                column: "FarmingAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_FarmManagerAssignments_ManagerUserId",
                schema: "be",
                table: "FarmManagerAssignments",
                column: "ManagerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FarmStaffAssignments_AssignedByUserId",
                schema: "be",
                table: "FarmStaffAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FarmStaffAssignments_FarmingAreaId",
                schema: "be",
                table: "FarmStaffAssignments",
                column: "FarmingAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_FarmStaffAssignments_ManagerUserId",
                schema: "be",
                table: "FarmStaffAssignments",
                column: "ManagerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FarmStaffAssignments_StaffUserId",
                schema: "be",
                table: "FarmStaffAssignments",
                column: "StaffUserId",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO be."FarmManagerAssignments"
                    ("Id", "ManagerUserId", "FarmingAreaId", "AssignedByUserId", "CreatedAt")
                SELECT gen_random_uuid(), u."Id", u."FarmingAreaId", area."OwnerId", NOW()
                FROM be."AppUsers" AS u
                INNER JOIN be."FarmingAreas" AS area ON area."Id" = u."FarmingAreaId"
                WHERE u."Role" = 'FarmManager'
                  AND u."FarmingAreaId" IS NOT NULL;

                INSERT INTO be."FarmStaffAssignments"
                    ("Id", "StaffUserId", "ManagerUserId", "FarmingAreaId", "AssignedByUserId", "CreatedAt")
                SELECT gen_random_uuid(), staff."Id", mgr."ManagerUserId", staff."FarmingAreaId", area."OwnerId", NOW()
                FROM be."AppUsers" AS staff
                INNER JOIN LATERAL (
                    SELECT assignment."ManagerUserId"
                    FROM be."FarmManagerAssignments" AS assignment
                    WHERE assignment."FarmingAreaId" = staff."FarmingAreaId"
                    ORDER BY assignment."CreatedAt"
                    LIMIT 1
                ) AS mgr ON TRUE
                INNER JOIN be."FarmingAreas" AS area ON area."Id" = staff."FarmingAreaId"
                WHERE staff."Role" IN ('FarmStaff', 'Staff')
                  AND staff."FarmingAreaId" IS NOT NULL;

                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (ORDER BY "CreatedAt", "Id") AS n
                    FROM be."AppUsers"
                    WHERE "Role" IN ('SystemAdministrator', 'SystemAdmin')
                )
                UPDATE be."AppUsers" AS u
                SET "UserCode" = 'SAD-' || LPAD(ranked.n::text, 5, '0')
                FROM ranked
                WHERE u."Id" = ranked."Id";

                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (ORDER BY "CreatedAt", "Id") AS n
                    FROM be."AppUsers"
                    WHERE "Role" IN ('FarmAdministrator', 'FarmOwner')
                )
                UPDATE be."AppUsers" AS u
                SET "UserCode" = 'FAD-' || LPAD(ranked.n::text, 5, '0')
                FROM ranked
                WHERE u."Id" = ranked."Id";

                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (ORDER BY "CreatedAt", "Id") AS n
                    FROM be."AppUsers"
                    WHERE "Role" = 'FarmManager'
                )
                UPDATE be."AppUsers" AS u
                SET "UserCode" = 'FMN-' || LPAD(ranked.n::text, 5, '0')
                FROM ranked
                WHERE u."Id" = ranked."Id";

                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (ORDER BY "CreatedAt", "Id") AS n
                    FROM be."AppUsers"
                    WHERE "Role" IN ('FarmStaff', 'Staff')
                )
                UPDATE be."AppUsers" AS u
                SET "UserCode" = 'FST-' || LPAD(ranked.n::text, 5, '0')
                FROM ranked
                WHERE u."Id" = ranked."Id";

                DO $$
                BEGIN
                    PERFORM setval('be.user_code_sad_seq', COALESCE((SELECT MAX(substring("UserCode" FROM '^SAD-([0-9]+)$')::bigint) FROM be."AppUsers"), 0) + 1, false);
                    PERFORM setval('be.user_code_fad_seq', COALESCE((SELECT MAX(substring("UserCode" FROM '^FAD-([0-9]+)$')::bigint) FROM be."AppUsers"), 0) + 1, false);
                    PERFORM setval('be.user_code_fmn_seq', COALESCE((SELECT MAX(substring("UserCode" FROM '^FMN-([0-9]+)$')::bigint) FROM be."AppUsers"), 0) + 1, false);
                    PERFORM setval('be.user_code_fst_seq', COALESCE((SELECT MAX(substring("UserCode" FROM '^FST-([0-9]+)$')::bigint) FROM be."AppUsers"), 0) + 1, false);
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "FarmingAreaId",
                schema: "be",
                table: "AppUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FarmManagerAssignments",
                schema: "be");

            migrationBuilder.DropTable(
                name: "FarmStaffAssignments",
                schema: "be");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_UserCode",
                schema: "be",
                table: "AppUsers");

            migrationBuilder.DropSequence(
                name: "user_code_fad_seq",
                schema: "be");

            migrationBuilder.DropSequence(
                name: "user_code_fmn_seq",
                schema: "be");

            migrationBuilder.DropSequence(
                name: "user_code_fst_seq",
                schema: "be");

            migrationBuilder.DropSequence(
                name: "user_code_sad_seq",
                schema: "be");

            migrationBuilder.RenameColumn(
                name: "UserCode",
                schema: "be",
                table: "AppUsers",
                newName: "EmployeeId");

            migrationBuilder.CreateSequence(
                name: "employee_id_sequence",
                schema: "be");

            migrationBuilder.AddColumn<Guid>(
                name: "FarmingAreaId",
                schema: "be",
                table: "AppUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_EmployeeId",
                schema: "be",
                table: "AppUsers",
                column: "EmployeeId",
                unique: true,
                filter: "\"EmployeeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_FarmingAreaId",
                schema: "be",
                table: "AppUsers",
                column: "FarmingAreaId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppUsers_FarmingAreas_FarmingAreaId",
                schema: "be",
                table: "AppUsers",
                column: "FarmingAreaId",
                principalSchema: "be",
                principalTable: "FarmingAreas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
