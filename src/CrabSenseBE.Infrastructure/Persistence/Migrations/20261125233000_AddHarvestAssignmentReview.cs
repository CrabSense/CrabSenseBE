using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125233000_AddHarvestAssignmentReview")]
public sealed class AddHarvestAssignmentReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "SubmittedAt", schema: "be", table: "HarvestAssignments", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "SubmissionNotes", schema: "be", table: "HarvestAssignments", type: "text", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ReviewedAt", schema: "be", table: "HarvestAssignments", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "ReviewedByUserId", schema: "be", table: "HarvestAssignments", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ReviewNotes", schema: "be", table: "HarvestAssignments", type: "text", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A rollback cannot leave a status unknown to the previous application.
        migrationBuilder.Sql("UPDATE be.\"HarvestAssignments\" SET \"Status\" = 'InProgress' WHERE \"Status\" = 'PendingReview';");
        migrationBuilder.DropColumn(name: "SubmittedAt", schema: "be", table: "HarvestAssignments");
        migrationBuilder.DropColumn(name: "SubmissionNotes", schema: "be", table: "HarvestAssignments");
        migrationBuilder.DropColumn(name: "ReviewedAt", schema: "be", table: "HarvestAssignments");
        migrationBuilder.DropColumn(name: "ReviewedByUserId", schema: "be", table: "HarvestAssignments");
        migrationBuilder.DropColumn(name: "ReviewNotes", schema: "be", table: "HarvestAssignments");
    }
}
