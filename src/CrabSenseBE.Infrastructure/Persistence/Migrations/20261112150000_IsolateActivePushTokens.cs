using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    public partial class IsolateActivePushTokens : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ambiguous device ownership is never guessed. Require re-registration.
            migrationBuilder.Sql("""
                UPDATE be."UserPushTokens" t SET "IsActive" = false
                WHERE t."IsActive" = true AND t."Token" IN (
                    SELECT "Token" FROM be."UserPushTokens" WHERE "IsActive" = true
                    GROUP BY "Token" HAVING count(*) > 1);
                """);
            migrationBuilder.CreateIndex("IX_UserPushTokens_Token", "UserPushTokens", "Token", schema: "be",
                unique: true, filter: "\"IsActive\" = true");
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex("IX_UserPushTokens_Token", "UserPushTokens", schema: "be");
        }
    }
}
