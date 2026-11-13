using System;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace CrabSenseBE.Infrastructure.Persistence.Migrations;
public partial class DiscoverControllerPorts : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<int>("CapabilityVersion","Devices",schema:"be",type:"integer",nullable:false,defaultValue:0);
        m.AddColumn<string>("CapabilityHash","Devices",schema:"be",type:"text",nullable:true);
        m.AddColumn<string>("HardwareModel","Devices",schema:"be",type:"text",nullable:true);
        m.CreateTable("ControllerPorts",schema:"be",columns:t=>new
        {
            Id=t.Column<Guid>(type:"uuid",nullable:false),
            DeviceId=t.Column<Guid>(type:"uuid",nullable:false),
            PortCode=t.Column<string>(type:"character varying(64)",maxLength:64,nullable:false),
            PortType=t.Column<string>(type:"character varying(32)",maxLength:32,nullable:false),
            Channel=t.Column<int>(type:"integer",nullable:true),
            Gpio=t.Column<int>(type:"integer",nullable:false),
            SecondaryGpio=t.Column<int>(type:"integer",nullable:true),
            ActiveLevel=t.Column<string>(type:"character varying(8)",maxLength:8,nullable:true),
            SensorCode=t.Column<string>(type:"character varying(128)",maxLength:128,nullable:true),
            ConfigVersion=t.Column<int>(type:"integer",nullable:false),
            IsPresent=t.Column<bool>(type:"boolean",nullable:false),
            ReportedOn=t.Column<bool>(type:"boolean",nullable:true),
            CommissioningStatus=t.Column<string>(type:"character varying(16)",maxLength:16,nullable:false),
            LastReportedAt=t.Column<DateTime>(type:"timestamp with time zone",nullable:false),
            CreatedAt=t.Column<DateTime>(type:"timestamp with time zone",nullable:false),
            UpdatedAt=t.Column<DateTime>(type:"timestamp with time zone",nullable:true)
        },constraints:t=>{t.PrimaryKey("PK_ControllerPorts",x=>x.Id);t.ForeignKey("FK_ControllerPorts_Devices_DeviceId",x=>x.DeviceId,principalTable:"Devices",principalColumn:"Id",principalSchema:"be",onDelete:ReferentialAction.Restrict);});
        m.CreateIndex("IX_ControllerPorts_DeviceId_PortCode","ControllerPorts",new[]{"DeviceId","PortCode"},schema:"be",unique:true);
        foreach(var table in new[]{"RasComponents","Sensors"})
        {
            m.AddColumn<Guid>("ControllerPortId",table,schema:"be",type:"uuid",nullable:true);
            m.CreateIndex("IX_"+table+"_ControllerPortId",table,"ControllerPortId",schema:"be",unique:table=="RasComponents");
            m.AddForeignKey("FK_"+table+"_ControllerPorts_ControllerPortId",table,"ControllerPortId","ControllerPorts",principalColumn:"Id",schema:"be",principalSchema:"be",onDelete:ReferentialAction.Restrict);
        }
    }
    protected override void Down(MigrationBuilder m)
    {
        throw new InvalidOperationException("Controller discovery data must not be discarded; restore a verified backup.");
    }
}
