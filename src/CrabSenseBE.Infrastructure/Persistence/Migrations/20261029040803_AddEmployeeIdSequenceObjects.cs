using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeIdSequenceObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "employee_id_sequence",
                schema: "be");

            migrationBuilder.Sql("""
                SELECT setval(
                    'be.employee_id_sequence'::regclass,
                    COALESCE(
                        MAX(substring("EmployeeId" from '^EMP-([0-9]+)$')::bigint),
                        0
                    ) + 1,
                    false
                    )
                FROM be."AppUsers";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_EmployeeId",
                schema: "be",
                table: "AppUsers",
                column: "EmployeeId",
                unique: true,
                filter: "\"EmployeeId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_EmployeeId",
                schema: "be",
                table: "AppUsers");

            migrationBuilder.DropSequence(
                name: "employee_id_sequence",
                schema: "be");
        }
    }
}
