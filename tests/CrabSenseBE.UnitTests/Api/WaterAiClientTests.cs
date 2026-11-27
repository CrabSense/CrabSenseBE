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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchUsesOneRequestAndIsolatesRejectedOrTamperedSamples(bool tamperIdentity)
    {
        var area=Guid.NewGuid();var camera=Guid.NewGuid();
        var samples=new[]{"NO2","NO3","NH3"}.Select(a=>new WaterAiClient.BatchSample(camera,a,"mg/L "+a,"lot",
            "signed-test",ExternalId,new(Guid.NewGuid(),[1,2,3],300,15),WaterAiClient.Algorithm)).ToArray();
        int calls=0;
        using var http=new HttpClient(new Handler(async request=>{
            calls++;
            Assert.Equal("/v1/batches/analyze",request.RequestUri!.AbsolutePath);
            Assert.Equal(Key,request.Headers.GetValues("X-AI-Key").Single());
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("scheduled",body.RootElement.GetProperty("mode").GetString());
            Assert.Equal(3,body.RootElement.GetProperty("samples").GetArrayLength());
            object Estimate(WaterAiClient.BatchSample s,bool badHash)=>new {status="estimated",analyte=s.Analyte,
                unit=s.Unit,algorithm=s.Algorithm,calibration_id=s.CalibrationId,
                media_id=tamperIdentity&&s.Analyte=="NH3"?samples[0].Photo.MediaId.ToString("D"):s.Photo.MediaId.ToString("D"),
                value=1m,holdout_max_error=0.1m,confidence=(decimal?)null,
                evidence=new{media_id=s.Photo.MediaId.ToString("D"),sha256=badHash?new string('0',64):
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s.Photo.Bytes)).ToLowerInvariant()}};
            return Response(new{batch_id="batch-test",status="partial",results=new object[]{
                Estimate(samples[2],true),new{status="rejected",analyte="NO3",media_id=samples[1].Photo.MediaId.ToString("D"),error="bad_image"},
                Estimate(samples[0],false)}});
        }));
        var ai=new WaterAiClient(http,Config());
        if(tamperIdentity)
            Assert.Equal(502,(await Assert.ThrowsAsync<AppException>(()=>ai.AnalyzeBatch(area,"batch-test","scheduled",samples,default))).StatusCode);
        else
        {
            var rows=await ai.AnalyzeBatch(area,"batch-test","scheduled",samples,default);
            Assert.Equal(1m,rows[0].Estimate!.Value);
            Assert.Equal(400,rows[1].ErrorStatus);
            Assert.Equal(502,rows[2].ErrorStatus);
        }
        Assert.Equal(1,calls);
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

    [Theory]
    [InlineData("equilibrium","mg/L TAN-N",0)]
    [InlineData("equilibrium","mg/L TAN-N",1)]
    [InlineData("equilibrium","mg/L NH4",0)]
    [InlineData("equilibrium","mg/L NH4",1)]
    [InlineData("printed_table","mg/L NH4",0)]
    [InlineData("printed_table","mg/L NH4",1)]
    [InlineData("printed_table","mg/L NH4",2)]
    [InlineData("printed_table","mg/L NH4",3)]
    public async Task AmmoniaUsesExactMicrosecondWireTimesAndRejectsChangedEcho(string method,string unit,int tamper)
    {
        var sample=new DateTimeOffset(2026,11,27,1,0,0,TimeSpan.FromHours(7)).AddTicks(1234567);
        var env=new Nh3Environment(8m,28m,15m,sample,sample.AddMilliseconds(100).AddTicks(1),
            sample.AddMilliseconds(200).AddTicks(2),sample.AddMilliseconds(300).AddTicks(3),10);
        var assessmentId=Guid.NewGuid();var mediaId=Guid.NewGuid();
        var expected=sample.ToUniversalTime().AddTicks(-7);
        using var http=new HttpClient(new Handler(async request=>{
            bool printed=method=="printed_table";
            Assert.Equal(printed?"/v1/ammonia/thuy-phuoc/lookup":"/v1/ammonia/convert",request.RequestUri!.AbsolutePath);
            using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var d=doc.RootElement;
            Assert.Equal(expected,d.GetProperty("sample_at").GetDateTimeOffset());
            Assert.Equal(expected.AddMilliseconds(100),d.GetProperty("ph_at").GetDateTimeOffset());
            Assert.Equal(expected.AddMilliseconds(200),d.GetProperty("temperature_at").GetDateTimeOffset());
            string Echo(DateTimeOffset time)=>time.ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz",System.Globalization.CultureInfo.InvariantCulture);
            var returnedSample=Echo(expected.AddTicks(tamper==1?10:0));
            if(printed)
                return Response(new{status="reference_only",revision="thuy-phuoc-photo-card-v1",method="printed_table_exact",
                    sample_id=assessmentId.ToString("D"),media_id=mediaId.ToString("D"),input=1m,input_basis="chart",
                    ph=8m,sample_at=returnedSample,ph_at=Echo(expected.AddMilliseconds(100).AddTicks(tamper==2?10:0)),
                    temperature_c=28m,temperature_at=Echo(expected.AddMilliseconds(200).AddTicks(tamper==3?10:0)),
                    chemical_accuracy_validated=false,crab_toxicity_threshold_validated=false,temperature_salinity_corrected=false,nh3_mg_l_printed=.05m});
            Assert.Equal(expected.AddMilliseconds(300).AddTicks(10),d.GetProperty("salinity_at").GetDateTimeOffset());
            Assert.Equal(unit,d.GetProperty("unit").GetString());
            const decimal fraction=.05m,n=14.00674m,h=1.00794m;
            decimal tan=unit=="mg/L TAN-N"?1m:n/(n+4*h)/(1-fraction);
            return Response(new{status="estimated",method="epa-1989-saltwater-whitfield-regression",sample_at=returnedSample,
                free_nh3_mg_l=tan*fraction*(n+3*h)/n,free_nh3_n_mg_l=tan*fraction,tan_n_mg_l=tan,unionized_fraction=fraction});
        }));
        var ai=new WaterAiClient(http,Config());
        Task<JsonElement> Call()=>ai.AmmoniaReference(method,assessmentId,mediaId,1m,unit,env,default);
        if(tamper!=0)Assert.Equal(502,(await Assert.ThrowsAsync<AppException>(Call)).StatusCode);
        else Assert.Equal(method=="printed_table"?"reference_only":"estimated",(await Call()).GetProperty("status").GetString());
        Assert.Equal(sample,env.SampleAt);
        Assert.Equal(7,env.SampleAt.Ticks%10);
    }
}
