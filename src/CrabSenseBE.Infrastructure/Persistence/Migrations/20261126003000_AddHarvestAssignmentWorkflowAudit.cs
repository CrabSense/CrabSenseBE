using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261126003000_AddHarvestAssignmentWorkflowAudit")]
public sealed class AddHarvestAssignmentWorkflowAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(name: "WorkflowAuditJson", schema: "be", table: "HarvestAssignments", type: "text", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "WorkflowAuditJson", schema: "be", table: "HarvestAssignments");
}
