using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;

namespace CrabSenseBE.UnitTests.Api;

// Normal unit runs explicitly skip this case; migration runner sets the isolated
// database and verifies the TRX contains one passed, non-skipped test.
public sealed class ScopedHistoryPostgresFactAttribute : FactAttribute
{
    public ScopedHistoryPostgresFactAttribute()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CRABSENSE_SCOPED_HISTORY_TEST_DB")))
            Skip="Run via the isolated PostgreSQL migration runner.";
    }
}

public sealed class ScopedSensorHistoryPostgresTests
{
    [ScopedHistoryPostgresFact]
    public async Task AuthenticatedIngestAndScopedHistoryUseMigratedPostgres()
    {
        var cs=Environment.GetEnvironmentVariable("CRABSENSE_SCOPED_HISTORY_TEST_DB")!;
        var cfg=new NpgsqlConnectionStringBuilder(cs);
        Assert.Equal("127.0.0.1",cfg.Host);
        Assert.Equal("scoped_history_test",cfg.Database);
        Assert.Equal("postgres",cfg.Username);
        // Match production migration discovery and history schema for every test context.
        void ConfigurePostgres(DbContextOptionsBuilder builder)=>builder.UseNpgsql(cs,npgsql=>{
            npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory","be");
            npgsql.CommandTimeout(120);
        });
        var optionsBuilder=new DbContextOptionsBuilder<AppDbContext>();
        ConfigurePostgres(optionsBuilder);
        var options=optionsBuilder.Options;
        await using var seed=new AppDbContext(options);
        Assert.Empty(await seed.Database.GetPendingMigrationsAsync());
        Assert.Contains("20261127100000_ScopedSensorHistory",await seed.Database.GetAppliedMigrationsAsync());
        var area=new FarmingArea{Code="HISTORY-A",Name="History A"};
        var other=new FarmingArea{Code="HISTORY-B",Name="History B"};
        var row=new FarmingRow{Code="HISTORY-ROW-A",Name="Row A",FarmingAreaId=area.Id};
        var nextRow=new FarmingRow{Code="HISTORY-ROW-B",Name="Row B",FarmingAreaId=other.Id};
        AppUser Actor(string name,UserRole role)=>new(){Username=name,Email=name+"@example.test",FullName=name,Role=role,IsActive=true};
        var admin=Actor("history-admin",UserRole.FarmAdministrator);
        var manager=Actor("history-manager",UserRole.FarmManager);
        var staff=Actor("history-staff",UserRole.FarmStaff);
        var platform=Actor("history-platform",UserRole.SystemAdministrator);
        var outsider=Actor("history-outsider",UserRole.FarmStaff);
        admin.ManagedAreas.Add(area);
        var staffAssignment=new FarmStaffAssignment{StaffUserId=staff.Id,ManagerUserId=manager.Id,FarmingAreaId=area.Id,AssignedByUserId=admin.Id};
        const string key="synthetic-history-integration-device-key";
        var device=new Device{DeviceCode="HISTORY-ESP",FarmingAreaId=area.Id,FarmingRowId=row.Id,EdgeState="Approved",
            CredentialHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))};
        var sensor=new Sensor{SensorCode="HISTORY-SAL",SensorType="Salinity",Unit="ppt",DeviceId=device.Id,FarmingAreaId=area.Id,FarmingRowId=row.Id};
        seed.FarmingAreas.AddRange(area,other);
        seed.FarmingRows.AddRange(row,nextRow);
        seed.AppUsers.AddRange(admin,manager,staff,platform,outsider);
        seed.FarmManagerAssignments.Add(new FarmManagerAssignment{ManagerUserId=manager.Id,FarmingAreaId=area.Id,AssignedByUserId=admin.Id});
        seed.FarmStaffAssignments.Add(staffAssignment);
        seed.Devices.Add(device);seed.Sensors.Add(sensor);
        // A historical legacy row must never be labelled from current sensor location.
        seed.WaterMeasurements.Add(new WaterMeasurement{SensorId=sensor.Id,Value=99m,Unit="ppt",
            MeasuredAt=DateTime.UtcNow.AddMinutes(-1),ReceivedAt=DateTime.UtcNow.AddMinutes(-1),DataQuality="Realtime",Source="realtime"});
        await seed.SaveChangesAsync();

        // Real MVC routes, authorization and DB repositories. Test-only user auth
        // and mocked alert/relay side effects avoid executing hardware commands.
        await using var api=PermissionApiFactory.Create(services=>{
            services.AddDbContext<AppDbContext>(ConfigurePostgres);
            services.AddScoped<IUnitOfWork,UnitOfWork>();
            services.AddScoped<IIotService,IotService>();
            services.AddSingleton(Mock.Of<IStorageService>());
            services.AddSingleton(Mock.Of<IAlertService>());
            services.AddSingleton(Mock.Of<IRasFlowService>());
        });
        async Task<HttpResponseMessage> Ingest(SensorDataRequest body,string deviceKey=key)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,"/api/iot/sensor-data");
            request.Headers.Add("X-Device-Key",deviceKey);
            request.Content=JsonContent.Create(body);
            return await api.Client.SendAsync(request);
        }
        var start=DateTime.UtcNow;
        var zero=new SensorDataRequest(device.DeviceCode,sensor.SensorCode,0m,"ppt",start.AddSeconds(-2),EventId:"history-zero");
        var second=zero with{Value=12m,MeasuredAt=start.AddSeconds(-3),EventId="history-second"};
        using(var denied=await Ingest(zero,"incorrect-key"))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        async Task Accepted(SensorDataRequest request)
        {
            using var response=await Ingest(request);
            Assert.True(response.IsSuccessStatusCode,$"Ingest HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
        await Accepted(zero);await Accepted(second);await Accepted(zero);
        await Accepted(zero with{Value=77m,MeasuredAt=start.AddMinutes(-5),EventId="history-backfill"});
        await Accepted(zero with{Value=88m,Unit=null,EventId="history-missing-unit"});
        await using(var verify=new AppDbContext(options))
        {
            var persisted=await verify.WaterMeasurements.AsNoTracking().Where(x=>x.SensorId==sensor.Id).ToListAsync();
            Assert.Equal(5,persisted.Count);
            Assert.Equal(2,persisted.Count(x=>x.ObservedAreaId==area.Id&&x.ObservedRowId==row.Id&&x.ObservedSensorType=="Salinity"));
            Assert.Single(persisted.Where(x=>x.Value==0m));
            Assert.All(persisted.Where(x=>x.Value is 77m or 88m or 99m),x=>Assert.Null(x.ObservedAreaId));
        }
        var to=DateTimeOffset.UtcNow;var from=to.AddHours(-1);
        string Time(DateTimeOffset value)=>Uri.EscapeDataString(value.ToString("O"));
        string Path(Guid a,Guid? r=null)=>$"/api/areas/{a}/sensors/{sensor.Id}/history?from={Time(from)}&to={Time(to)}"+(r.HasValue?$"&farmingRowId={r.Value}":"");
        async Task<JsonElement> Read(AppUser user,string path)
        {
            using var response=await api.SendAsync(user.Role.ToString(),user.Id,HttpMethod.Get,path);
            var body=await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode,$"History HTTP {(int)response.StatusCode}: {body}");
            Assert.Equal("no-store",response.Headers.CacheControl?.ToString());
            Assert.DoesNotContain("history-zero",body);
            Assert.DoesNotContain("edgeEventKey",body);
            using var json=JsonDocument.Parse(body);
            return json.RootElement.GetProperty("data").Clone();
        }
        var path=Path(area.Id,row.Id);
        foreach(var actor in new[]{admin,manager,staff})
        {
            var history=await Read(actor,path);
            Assert.Equal("scoped-sensor-history-v1",history.GetProperty("contract").GetString());
            Assert.Equal(new[]{0m,12m},history.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("value").GetDecimal()).ToArray());
            Assert.All(history.GetProperty("items").EnumerateArray(),x=>{
                Assert.Equal(area.Id,x.GetProperty("farmingAreaId").GetGuid());
                Assert.Equal(row.Id,x.GetProperty("farmingRowId").GetGuid());
                Assert.Equal("ppt",x.GetProperty("unit").GetString());
                Assert.Equal("Realtime",x.GetProperty("dataQuality").GetString());
            });
        }
        async Task Status(AppUser? actor,string uri,HttpStatusCode expected)
        {
            using var response=await api.SendAsync(actor?.Role.ToString(),actor?.Id,HttpMethod.Get,uri);
            Assert.Equal(expected,response.StatusCode);
        }
        await Status(null,path,HttpStatusCode.Unauthorized);
        await Status(platform,path,HttpStatusCode.Forbidden);
        await Status(outsider,path,HttpStatusCode.Forbidden);
        await Status(staff,Path(other.Id),HttpStatusCode.Forbidden);
        await Status(staff,Path(area.Id,nextRow.Id),HttpStatusCode.NotFound);
        await Status(staff,path+"&page=2",HttpStatusCode.BadRequest);
        var page1=await Read(staff,path+"&pageSize=1");
        Assert.True(page1.GetProperty("hasMore").GetBoolean());
        var cutoff=page1.GetProperty("asOf").GetDateTimeOffset();
        var page2=await Read(staff,path+"&page=2&pageSize=1&asOf="+Time(cutoff));
        Assert.False(page2.GetProperty("hasMore").GetBoolean());
        Assert.Equal(12m,Assert.Single(page2.GetProperty("items").EnumerateArray().ToArray()).GetProperty("value").GetDecimal());

        // Actual assignment removal is checked on the next request.
        seed.FarmStaffAssignments.Remove(staffAssignment);
        await seed.SaveChangesAsync();
        await Status(staff,path,HttpStatusCode.Forbidden);
        // Moving the device/sensor to another area must never relabel historical rows.
        await using(var move=new AppDbContext(options))
        {
            var d=await move.Devices.SingleAsync(x=>x.Id==device.Id);
            var s=await move.Sensors.SingleAsync(x=>x.Id==sensor.Id);
            d.FarmingAreaId=other.Id;d.FarmingRowId=nextRow.Id;
            s.FarmingAreaId=other.Id;s.FarmingRowId=nextRow.Id;
            var a=await move.AppUsers.Include(x=>x.ManagedAreas).SingleAsync(x=>x.Id==admin.Id);
            a.ManagedAreas.Add(await move.FarmingAreas.SingleAsync(x=>x.Id==other.Id));
            await move.SaveChangesAsync();
        }
        await Status(admin,path,HttpStatusCode.NotFound);
        var moved=await Read(admin,Path(other.Id,nextRow.Id));
        Assert.Empty(moved.GetProperty("items").EnumerateArray().ToArray());
        await Accepted(zero with{Value=15m,MeasuredAt=DateTime.UtcNow.AddSeconds(-1),EventId="history-after-move"});
        to=DateTimeOffset.UtcNow;from=to.AddHours(-1);
        var after=await Read(admin,Path(other.Id,nextRow.Id));
        Assert.Equal(15m,Assert.Single(after.GetProperty("items").EnumerateArray().ToArray()).GetProperty("value").GetDecimal());
    }
}
