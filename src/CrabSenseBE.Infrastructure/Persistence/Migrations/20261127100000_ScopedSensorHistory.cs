using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127100000_ScopedSensorHistory")]
public sealed class ScopedSensorHistory : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<Guid>("ObservedAreaId","WaterMeasurements",schema:"be",type:"uuid",nullable:true);
        m.AddColumn<Guid>("ObservedRowId","WaterMeasurements",schema:"be",type:"uuid",nullable:true);
        m.AddColumn<string>("ObservedSensorType","WaterMeasurements",schema:"be",type:"text",nullable:true);
        m.CreateIndex(name:"IX_WaterMeasurements_ObservedAreaId_SensorId_MeasuredAt",schema:"be",
            table:"WaterMeasurements",columns:new[]{"ObservedAreaId","SensorId","MeasuredAt"},filter:"\"ObservedAreaId\" IS NOT NULL");
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropIndex(name:"IX_WaterMeasurements_ObservedAreaId_SensorId_MeasuredAt",schema:"be",table:"WaterMeasurements");
        m.DropColumn(name:"ObservedAreaId",schema:"be",table:"WaterMeasurements");
        m.DropColumn(name:"ObservedRowId",schema:"be",table:"WaterMeasurements");
        m.DropColumn(name:"ObservedSensorType",schema:"be",table:"WaterMeasurements");
    }
}
