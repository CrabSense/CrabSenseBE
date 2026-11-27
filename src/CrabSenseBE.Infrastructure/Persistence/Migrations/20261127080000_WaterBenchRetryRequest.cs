using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261127080000_WaterBenchRetryRequest")]
public sealed class WaterBenchRetryRequest : Migration
{
    protected override void Up(MigrationBuilder m)=>m.AddColumn<string>(name:"RequestJson",schema:"be",table:"WaterBenchAiAttempts",type:"text",nullable:true);
    protected override void Down(MigrationBuilder m)=>m.DropColumn(name:"RequestJson",schema:"be",table:"WaterBenchAiAttempts");
}
