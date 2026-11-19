using System.Net;
using System.Text;
using System.Text.Json;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterAiClientTests
{
    private const string Key="test-key-not-production-01234567890123456789";
    private static readonly string ExternalId=new('a',64);
    private static IConfiguration Config()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
        ["WaterAi:BaseUrl"]="https://water-ai.internal",["WaterAi:ApiKey"]=Key}).Build();
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> run):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>run(request);
    }
    private static HttpResponseMessage Response(object body)=>new(HttpStatusCode.OK){
        Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")};

    [Fact]
    public async Task FitSendsSnakeCaseAndPersistsSelectedAlgorithm()
    {
        var area=Guid.NewGuid();var camera=Guid.NewGuid();var media=Guid.NewGuid();
        var expiry=DateTime.UtcNow.AddDays(1);
        using var http=new HttpClient(new Handler(async request=>{
            Assert.Equal("/v1/calibrations/fit",request.RequestUri!.AbsolutePath);
            Assert.Equal(Key,request.Headers.GetValues("X-AI-Key").Single());
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var d=body.RootElement;
            Assert.Equal(area.ToString("D"),d.GetProperty("area_id").GetString());
            Assert.Equal(camera.ToString("D"),d.GetProperty("camera_id").GetString());
            Assert.Equal(300,d.GetProperty("protocol").GetProperty("reaction_seconds").GetInt32());
            Assert.Equal("AQID",d.GetProperty("validation")[0].GetProperty("image_b64").GetString());
            return Response(new { calibration_token="signed-test",calibration_id=ExternalId,algorithm=WaterAiClient.Algorithm,
                unit="mg/L NO2",valid_until=expiry,curve=new{channel=1},validation=new[]{new{media_id=media.ToString("D"),absolute_error=0.01m}}});
        }));
        var ai=new WaterAiClient(http,Config());
        var protocol=new ColorProtocol(100,100,new(0,0,20,20),new(50,50,20,20),"G",300,20,5,"kit",10,20);
        var p=new WaterAiClient.Photo(media,[1,2,3],300,15,0.5m);
        var result=await ai.Fit(area,camera,"NO2","Sera","04410-2015","mg/L NO2","lot",protocol,
            [p with {MediaId=Guid.NewGuid(),Concentration=0},p with {MediaId=Guid.NewGuid(),Concentration=1},p with {MediaId=Guid.NewGuid(),Concentration=2}],p,0.1m,expiry,default);
        Assert.Equal(ExternalId,result.Id);Assert.Equal("signed-test",result.Token);
        Assert.Equal(1,result.Curve.GetProperty("channel").GetInt32());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnalyzeChecksImageAndUnitBeforeReturningEstimate(bool tamper)
    {
        var photo=new WaterAiClient.Photo(Guid.NewGuid(),[1,2,3],300,15);
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo.Bytes)).ToLowerInvariant();
        using var http=new HttpClient(new Handler(async request=>{
            Assert.Equal("/v1/analyze",request.RequestUri!.AbsolutePath);
            using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("signed-test",doc.RootElement.GetProperty("calibration_token").GetString());
            return Response(new {status="estimated",analyte="NO2",unit="mg/L NO2",algorithm=WaterAiClient.Algorithm,
                calibration_id=ExternalId,media_id=photo.MediaId.ToString("D"),value=1.5m,holdout_max_error=0.01m,confidence=(decimal?)null,
                evidence=new{media_id=photo.MediaId.ToString("D"),sha256=tamper?new string('0',64):hash}});
        }));
        var ai=new WaterAiClient(http,Config());
        Task<WaterAiClient.Estimate> Call()=>ai.Analyze(Guid.NewGuid(),Guid.NewGuid(),"NO2","mg/L NO2","lot","signed-test",ExternalId,photo,default);
        if(tamper)Assert.Equal(502,(await Assert.ThrowsAsync<AppException>(Call)).StatusCode);
        else Assert.Equal(1.5m,(await Call()).Value);
    }
    [Fact]
    public async Task UnavailableDoesNotFallbackAndTokenIsNotPublic()
    {
        using var http=new HttpClient(new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var ai=new WaterAiClient(http,Config());
        var ex=await Assert.ThrowsAsync<AppException>(()=>ai.Analyze(Guid.NewGuid(),Guid.NewGuid(),"NO2","mg/L NO2","lot","signed-test",ExternalId,
            new(Guid.NewGuid(),[1,2,3],300,15),default));
        Assert.Equal(503,ex.StatusCode);
        Assert.DoesNotContain("signed-test",JsonSerializer.Serialize(new WaterColorCalibration{AiCalibrationToken="signed-test"}));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ControlRejectsInconsistentVerdict(bool tamper)
    {
        var photo=new WaterAiClient.Photo(Guid.NewGuid(),[1,2,3],300,15);
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo.Bytes)).ToLowerInvariant();
        using var http=new HttpClient(new Handler(async request=>{
            Assert.Equal("/v1/calibrations/validate",request.RequestUri!.AbsolutePath);
            using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(1m,doc.RootElement.GetProperty("expected").GetDecimal());
            return Response(new { status=tamper?"passed":"failed",algorithm=WaterAiClient.Algorithm,calibration_id=ExternalId,analyte="NO2",unit="mg/L NO2",
                media_id=photo.MediaId.ToString("D"),expected=1m,estimated=1.5m,absolute_error=0.5m,max_error=0.1m,
                evidence=new{media_id=photo.MediaId.ToString("D"),sha256=hash}});
        }));
        var ai=new WaterAiClient(http,Config());
        Task<WaterAiClient.Control> Call()=>ai.ValidateControl(Guid.NewGuid(),Guid.NewGuid(),"NO2","mg/L NO2","lot","signed-test",ExternalId,photo,1m,0.1m,default);
        if(tamper)Assert.Equal(502,(await Assert.ThrowsAsync<AppException>(Call)).StatusCode);
        else Assert.Equal("failed",(await Call()).Status);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeometryVersionRequiresAlignedEvidence(bool aligned)
    {
        var photo=new WaterAiClient.Photo(Guid.NewGuid(),[1,2,3],300,15);
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo.Bytes)).ToLowerInvariant();
        using var http=new HttpClient(new Handler(_=>Task.FromResult(Response(new {
            status="estimated",analyte="NO2",unit="mg/L NO2",algorithm=WaterAiClient.GeometryAlgorithm,
            calibration_id=ExternalId,media_id=photo.MediaId.ToString("D"),value=1.5m,holdout_max_error=0.01m,confidence=(decimal?)null,
            evidence=new{media_id=photo.MediaId.ToString("D"),sha256=hash,geometry=new{status=aligned?"aligned":"rejected",shift_px=new[]{0,0}}}
        }))));
        var ai=new WaterAiClient(http,Config());
        Task<WaterAiClient.Estimate> Call()=>ai.Analyze(Guid.NewGuid(),Guid.NewGuid(),"NO2","mg/L NO2","lot","signed-test",ExternalId,photo,default,WaterAiClient.GeometryAlgorithm);
        if(aligned)Assert.Equal(1.5m,(await Call()).Value);
        else Assert.Equal(502,(await Assert.ThrowsAsync<AppException>(Call)).StatusCode);
    }
}
