using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    public partial class ProtectFarmStateConcurrency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>("ConcurrencyVersion", "Boxes", schema: "be", type: "bigint", nullable: false, defaultValue: 0L);
            migrationBuilder.AddColumn<long>("ConcurrencyVersion", "Crabs", schema: "be", type: "bigint", nullable: false, defaultValue: 0L);
            migrationBuilder.Sql("""
                CREATE FUNCTION be.bump_farm_state_version() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    NEW."ConcurrencyVersion" := OLD."ConcurrencyVersion" + 1;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_boxes_state_version BEFORE UPDATE ON be."Boxes"
                    FOR EACH ROW EXECUTE FUNCTION be.bump_farm_state_version();
                CREATE TRIGGER trg_crabs_state_version BEFORE UPDATE ON be."Crabs"
                    FOR EACH ROW EXECUTE FUNCTION be.bump_farm_state_version();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER trg_crabs_state_version ON be."Crabs";
                DROP TRIGGER trg_boxes_state_version ON be."Boxes";
                DROP FUNCTION be.bump_farm_state_version();
                """);
            migrationBuilder.DropColumn("ConcurrencyVersion", "Crabs", schema: "be");
            migrationBuilder.DropColumn("ConcurrencyVersion", "Boxes", schema: "be");
        }
    }
}
