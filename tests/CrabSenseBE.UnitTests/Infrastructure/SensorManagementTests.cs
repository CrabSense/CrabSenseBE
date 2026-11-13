using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;

namespace CrabSenseBE.UnitTests.Infrastructure;
public sealed class SensorManagementTests
{
    private static AppDbContext Db()=>new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static SensorManagementController Controller(AppDbContext db)=>new(db)
    {
        ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity(
            new[]{new Claim(ClaimTypes.NameIdentifier,Guid.NewGuid().ToString())},"test"))}}
    };
    [Fact]
    public async Task Disabled_or_missing_port_cannot_ingest_and_backfill_does_not_drive_automation()
    {
        await using var db=Db();
        var d=new Device{DeviceCode="esp",CapabilityVersion=1};
        var p=new ControllerPort{DeviceId=d.Id,PortCode="P1",PortType="ANALOG_INPUT",Gpio=34,ConfigVersion=1,IsPresent=true,SensorCode="ph"};
        var s=new Sensor{DeviceId=d.Id,ControllerPortId=p.Id,SensorCode="ph",SensorType="pH",Unit="pH",IsActive=false};
        db.Devices.Add(d);db.ControllerPorts.Add(p);db.Sensors.Add(s);await db.SaveChangesAsync();
        var flow=new Mock<IRasFlowService>();var alerts=new Mock<IAlertService>();
        var service=new IotService(new UnitOfWork(db),Mock.Of<IStorageService>(),alerts.Object,flow.Object,NullLogger<IotService>.Instance);
        var req=new SensorDataRequest(d.DeviceCode,s.SensorCode,7m,"pH",DateTime.UtcNow);
        Assert.Equal(409,(await Assert.ThrowsAsync<AppException>(()=>service.IngestSensorDataAsync(req,d.Id,d.DeviceCode))).StatusCode);
        s.IsActive=true;p.IsPresent=false;await db.SaveChangesAsync();
        Assert.Equal(409,(await Assert.ThrowsAsync<AppException>(()=>service.IngestSensorDataAsync(req,d.Id,d.DeviceCode))).StatusCode);
        p.IsPresent=true;await db.SaveChangesAsync();
        Assert.Equal(400,(await Assert.ThrowsAsync<AppException>(()=>service.IngestSensorDataAsync(req with {Unit="mg/L"},d.Id,d.DeviceCode))).StatusCode);
        await service.IngestSensorDataAsync(req with{MeasuredAt=DateTime.UtcNow.AddHours(-2)},d.Id,d.DeviceCode);
        Assert.Equal("Backfill",(await db.WaterMeasurements.SingleAsync()).DataQuality);
        Assert.Null(s.LastSeenAt);
        flow.Verify(x=>x.ApplyAutoRelaysAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task Register_port_and_configuration_with_manual_calibration_are_persisted()
    {
        await using var db=Db();
        var d=new Device{DeviceCode="esp",CapabilityVersion=1};
        var p=new ControllerPort{DeviceId=d.Id,PortCode="ADC",PortType="ANALOG_INPUT",Gpio=34,ConfigVersion=1,IsPresent=true,SensorCode="ph"};
        db.Devices.Add(d);db.ControllerPorts.Add(p);await db.SaveChangesAsync();
        var api=Controller(db);
        await api.Register(new RegisterSensorFromPort(d.Id,p.Id,"ph","pH","pH"),default);
        var s=await db.Sensors.SingleAsync();
        Assert.Equal(p.Id,s.ControllerPortId);
        await api.Configuration(s.Id,new SensorConfigurationRequest(10,60,s.ConfigurationVersion),default);
        Assert.Equal(10,s.SamplingIntervalSeconds);Assert.Null(s.AppliedConfigurationVersion);
        await api.Calibration(s.Id,new SensorCalibrationRequest(DateTimeOffset.UtcNow,7,7.1m,"pH","buffer","Passed"),default);
        Assert.Single(await db.SensorCalibrations.ToListAsync());
        Assert.Contains(await db.OperationLogs.ToListAsync(),x=>x.Action=="RecordSensorCalibration");
        await api.Disable(s.Id,default);Assert.False(s.IsActive);Assert.Null(s.LastSeenAt);
        await api.Enable(s.Id,default);Assert.True(s.IsActive);Assert.Null(s.LastSeenAt);
    }
    [Fact]
    public async Task Bus_register_must_be_unique_and_output_is_not_sensor_input()
    {
        await using var db=Db();
        var d=new Device{DeviceCode="esp",CapabilityVersion=1};
        var bus=new ControllerPort{DeviceId=d.Id,PortCode="BUS",PortType="RS485",Gpio=16,SecondaryGpio=17,ConfigVersion=1,IsPresent=true};
        var output=new ControllerPort{DeviceId=d.Id,PortCode="OUT",PortType="DIGITAL_OUTPUT",Gpio=19,ConfigVersion=1,IsPresent=true};
        db.Devices.Add(d);db.ControllerPorts.AddRange(bus,output);await db.SaveChangesAsync();
        var api=Controller(db);
        await api.Register(new RegisterSensorFromPort(d.Id,bus.Id,"ph","pH","pH",BusAddress:"1",BusRegister:"40001"),default);
        await api.Register(new RegisterSensorFromPort(d.Id,bus.Id,"temp","Temperature","C",BusAddress:"1",BusRegister:"40002"),default);
        Assert.Equal(409,(await Assert.ThrowsAsync<AppException>(()=>api.Register(new RegisterSensorFromPort(d.Id,bus.Id,"duplicate","pH","pH",BusAddress:"1",BusRegister:"40001"),default))).StatusCode);
        Assert.Equal(409,(await Assert.ThrowsAsync<AppException>(()=>api.Register(new RegisterSensorFromPort(d.Id,output.Id,"bad","pH","pH"),default))).StatusCode);
        Assert.Equal(2,await db.Sensors.CountAsync());
    }
}
