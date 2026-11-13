using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>Repairs legacy bootstrap schemas that limited IncidentId to 64 characters.
/// The EF model has always expected text for this property.</summary>
public partial class RepairAlertIncidentIdText : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $incident_id$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'be' AND table_name = 'Alerts'
                      AND column_name = 'IncidentId' AND data_type = 'character varying'
                ) THEN
                    ALTER TABLE be."Alerts" ALTER COLUMN "IncidentId" TYPE text;
                END IF;
            END
            $incident_id$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep text: the preceding EF model also expects text. Narrowing would
        // destroy valid incident identifiers written after the repair.
    }
}
