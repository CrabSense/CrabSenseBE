using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127060000_WaterBenchAiAttempts")]
public sealed class AddWaterBenchAiAttempts : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE be."WaterBenchAiAttempts" (
            "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NULL,
            "SessionEvidenceId" uuid NOT NULL REFERENCES be."WaterBenchEvidence"("Id") ON DELETE RESTRICT,
            "RequestId" uuid NOT NULL, "RequestHash" varchar(64) NOT NULL,
            "Number" integer NOT NULL, "State" varchar(32) NOT NULL,
            "LeaseUntil" timestamptz NOT NULL, "RetryAfter" timestamptz NULL,
            CONSTRAINT "CK_WaterBenchAiAttempt_State" CHECK ("Number" BETWEEN 1 AND 3 AND "RequestHash" ~ '^[0-9a-f]{64}$' AND "State" IN ('running','retryable','retest_required','contract_blocked','succeeded'))
        );
        CREATE UNIQUE INDEX "IX_WaterBenchAiAttempts_SessionEvidenceId_Number" ON be."WaterBenchAiAttempts"("SessionEvidenceId","Number");
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""DROP TABLE be."WaterBenchAiAttempts";""");
}
