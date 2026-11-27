using System.Text.Json;
using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace CrabSenseBE.UnitTests.Api;

public sealed class WaterBenchTechnicalTests
{
    [Fact]
    public void TechnicalRouteIsPlatformOnlyAndReadOnly()
    {
        var type=typeof(WaterBenchTechnicalController);
        Assert.Equal(AppRoles.Platform,type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        var action=type.GetMethod(nameof(WaterBenchTechnicalController.Stations))!;
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Empty(type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)
            .Where(m=>m.GetCustomAttribute<HttpPostAttribute>()!=null||m.GetCustomAttribute<HttpPutAttribute>()!=null||
                m.GetCustomAttribute<HttpPatchAttribute>()!=null||m.GetCustomAttribute<HttpDeleteAttribute>()!=null));
    }

    [Fact]
    public async Task TechnicalProjectionExcludesRawEvidenceAndFarmResults()
    {
        var options=new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db=new AppDbContext(options);
        var now=DateTime.UtcNow;
        var k=new FarmKiosk{Code="KIOSK-TEST",Name="PRIVATE_AREA_NAME",Status="Online",LastSeenAt=now,
            LanIp="PRIVATE_IP",SoftwareVersion="1.2.3",FarmingAreaId=Guid.NewGuid()};
        var s=new WaterBenchStation{KioskId=k.Id,MasterHardwareId="001122334455",SlaveHardwareId="001122334466",
            DailyTimesJson="[\"PRIVATE_SCHEDULE\"]",SampleSource="PRIVATE_SOURCE",TargetsJson="[\"PRIVATE_TARGET\"]"};
        var error=new WaterBenchEvidence{StationId=s.Id,Kind="camera_error",CreatedAt=now.AddMinutes(-5),PayloadJson="{\"private\":\"PRIVATE_PAYLOAD\"}"};
        var old=new WaterBenchEvidence{StationId=s.Id,Kind="camera_error",CreatedAt=now.AddHours(-25)};
        var session=new WaterBenchEvidence{StationId=s.Id,Kind="session",CreatedAt=now,Analyte="NH3",PayloadJson="{\"secret\":\"PRIVATE_SAMPLE\"}"};
        db.FarmKiosks.Add(k);
        db.WaterBenchStations.Add(s);
        db.WaterBenchEvidence.AddRange(error,old,session);
        db.WaterBenchAiAttempts.Add(new WaterBenchAiAttempt{SessionEvidenceId=session.Id,State="contract_blocked",
            CreatedAt=now,RequestJson="{\"private\":\"PRIVATE_REQUEST\"}"});
        await db.SaveChangesAsync();
        var controller=new WaterBenchTechnicalController(db){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        var result=Assert.IsType<OkObjectResult>(await controller.Stations());
        var text=JsonSerializer.Serialize(result.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var json=JsonDocument.Parse(text);
        var data=json.RootElement.GetProperty("data");
        var row=Assert.Single(data.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal(1,row.GetProperty("cameraErrors24h").GetInt32());
        Assert.Equal(1,row.GetProperty("contractBlocked24h").GetInt32());
        Assert.Equal("stale",row.GetProperty("technicalReceiptFreshness").GetString());
        Assert.False(data.GetProperty("liveHardwareVerified").GetBoolean());
        Assert.False(data.GetProperty("chemicalDataIncluded").GetBoolean());
        Assert.DoesNotContain("PRIVATE_",text);
        Assert.DoesNotContain("NH3",text);
        Assert.DoesNotContain("candidateValue",text);
        Assert.DoesNotContain("farmingAreaId",text);
        Assert.Equal("no-store",controller.Response.Headers["Cache-Control"].ToString());
        await Assert.ThrowsAsync<AppException>(()=>controller.Stations(pageSize:51));
        var empty=Assert.IsType<OkObjectResult>(await controller.Stations(page:2));
        using var emptyJson=JsonDocument.Parse(JsonSerializer.Serialize(empty.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(0,emptyJson.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
    }
}
