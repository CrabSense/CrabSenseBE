using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Services;

namespace CrabSenseBE.Api.Services;

/// <summary>Private service-to-service adapter. Never accepts a URL or credential from a farm request.</summary>
public sealed class WaterAiClient(HttpClient http, IConfiguration config)
{
    public const string Algorithm = "rgb-median-monotonic-v1";
    public const string GeometryAlgorithm = "rgb-median-monotonic-v2-qc";
    public static bool Supported(string? algorithm)=>algorithm is Algorithm or GeometryAlgorithm;
    private static bool GeometryEvidence(JsonElement evidence,string algorithm)=>algorithm!=GeometryAlgorithm||
        (evidence.TryGetProperty("geometry",out var g)&&g.GetProperty("status").GetString()=="aligned"&&
         g.GetProperty("shift_px").GetArrayLength()==2&&g.GetProperty("shift_px")[0].GetInt32()==0&&g.GetProperty("shift_px")[1].GetInt32()==0);
    public record Photo(Guid MediaId, byte[] Bytes, int ReactionSeconds, decimal SalinityPpt, decimal? Concentration = null);
    public record Fitted(string Token, string Id, string Algorithm, JsonElement Curve, JsonElement Validation, DateTime ValidUntil);
    public record Estimate(decimal Value, JsonElement Evidence, decimal HoldoutError);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static object Roi(ColorRoi r) => new { x=r.X,y=r.Y,width=r.Width,height=r.Height };
    private static object Protocol(ColorProtocol p) => new { width=p.Width,height=p.Height,sample=Roi(p.Sample),reference=Roi(p.Reference),
        reaction_seconds=p.ReactionSeconds,tolerance_seconds=p.TimingToleranceSeconds,sample_volume_ml=p.SampleVolumeMl,
        min_salinity_ppt=p.MinSalinityPpt,max_salinity_ppt=p.MaxSalinityPpt,
        geometry_policy=p.GeometryPolicy is { } g ? new { anchor=Roi(g.Anchor),max_shift=g.MaxShift,min_correlation=g.MinCorrelation,min_focus_ratio=g.MinFocusRatio } : null };
    private static object Standard(Photo p) => new { image_b64=Convert.ToBase64String(p.Bytes),media_id=p.MediaId.ToString("D"),
        reaction_seconds=p.ReactionSeconds,salinity_ppt=p.SalinityPpt,concentration=p.Concentration };
    public async Task<Fitted> Fit(Guid area, Guid camera, string analyte, string kit, string revision, string unit, string lot,
        ColorProtocol protocol, IReadOnlyList<Photo> standards, Photo validation, decimal tolerance, DateTime validUntil,CancellationToken ct)
    {
        if(standards.Sum(x=>(long)x.Bytes.Length)+validation.Bytes.Length>28L*1024*1024)
            throw AppException.BadRequest("Tổng ảnh hiệu chuẩn vượt giới hạn 28 MiB; giảm kích thước ảnh.");
        var data=await Post("v1/calibrations/fit",new { area_id=area.ToString("D"),camera_id=camera.ToString("D"),analyte,kit,
            kit_revision=revision,unit,reagent_lot=lot,protocol=Protocol(protocol),standards=standards.Select(Standard),
            validation=new[]{Standard(validation)},max_validation_error=tolerance,valid_until=validUntil },ct);
        try
        {
            var token=data.GetProperty("calibration_token").GetString();var id=data.GetProperty("calibration_id").GetString();
            var algo=data.GetProperty("algorithm").GetString();var expiry=data.GetProperty("valid_until").GetDateTimeOffset().UtcDateTime;
            var curve=data.GetProperty("curve");var checks=data.GetProperty("validation");
            if(string.IsNullOrWhiteSpace(token)||token.Length>100000||!Hash(id)||algo!=(protocol.GeometryPolicy is null?Algorithm:GeometryAlgorithm)||
                data.GetProperty("unit").GetString()!=unit||Math.Abs((expiry-validUntil).TotalSeconds)>1||
                curve.ValueKind!=JsonValueKind.Object||curve.GetProperty("channel").GetInt32() is <0 or >2||checks.ValueKind!=JsonValueKind.Array||checks.GetArrayLength()!=1||
                checks[0].GetProperty("media_id").GetString()!=validation.MediaId.ToString("D")||
                checks[0].GetProperty("absolute_error").GetDecimal()<0||checks[0].GetProperty("absolute_error").GetDecimal()>tolerance)
                throw Invalid();
            return new(token,id!,algo!,curve.Clone(),checks.Clone(),expiry);
        }
        catch(Exception ex) when(ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){throw Invalid();}
    }
    public async Task<Estimate> Analyze(Guid area, Guid camera, string analyte,string unit,string lot,string token,string calibrationId,Photo photo,CancellationToken ct,string algorithm=Algorithm)
    {
        var sample=new BatchSample(camera,analyte,unit,lot,token,calibrationId,photo,algorithm);
        if(!Supported(algorithm))throw Invalid();
        var data=await Post("v1/analyze",AnalyzePayload(area,sample),ct);
        return ReadEstimate(data,sample);
    }
    public sealed record BatchSample(Guid CameraId,string Analyte,string Unit,string Lot,string Token,string CalibrationId,Photo Photo,string Algorithm);
    public sealed record BatchOutcome(string Analyte,Estimate? Estimate,int? ErrorStatus=null);
    private static object AnalyzePayload(Guid area,BatchSample s)=>new {area_id=area.ToString("D"),camera_id=s.CameraId.ToString("D"),
        analyte=s.Analyte,reagent_lot=s.Lot,calibration_token=s.Token,image_b64=Convert.ToBase64String(s.Photo.Bytes),
        media_id=s.Photo.MediaId.ToString("D"),reaction_seconds=s.Photo.ReactionSeconds,salinity_ppt=s.Photo.SalinityPpt};
    private static Estimate ReadEstimate(JsonElement data,BatchSample sample)
    {
        var (analyte,unit,calibrationId,photo,algorithm)=(sample.Analyte,sample.Unit,sample.CalibrationId,sample.Photo,sample.Algorithm);
        try
        {
            var value=data.GetProperty("value").GetDecimal();var error=data.GetProperty("holdout_max_error").GetDecimal();
            var evidence=data.GetProperty("evidence");
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo.Bytes));
            if(data.GetProperty("status").GetString()!="estimated"||data.GetProperty("analyte").GetString()!=analyte||
                data.GetProperty("unit").GetString()!=unit||data.GetProperty("algorithm").GetString()!=algorithm||
                data.GetProperty("calibration_id").GetString()!=calibrationId||data.GetProperty("media_id").GetString()!=photo.MediaId.ToString("D")||
                value<0||value>10000||error<0||error>1000||data.GetProperty("confidence").ValueKind!=JsonValueKind.Null||
                evidence.GetProperty("media_id").GetString()!=photo.MediaId.ToString("D")||!GeometryEvidence(evidence,algorithm)||
                !string.Equals(evidence.GetProperty("sha256").GetString(),hash,StringComparison.OrdinalIgnoreCase))throw Invalid();
            return new(value,evidence.Clone(),error);
        }
        catch(Exception ex) when(ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){throw Invalid();}
    }
    // Python dispatches the samples concurrently inside one admitted service request.
    public async Task<IReadOnlyList<BatchOutcome>> AnalyzeBatch(Guid area,string batchId,string mode,IReadOnlyList<BatchSample> samples,CancellationToken ct)
    {
        if(area==Guid.Empty||string.IsNullOrWhiteSpace(batchId)||batchId.Length>128||mode is not ("manual" or "scheduled")||
            samples.Count is <1 or >3||samples.Select(x=>x.Analyte).Distinct(StringComparer.Ordinal).Count()!=samples.Count||
            samples.Select(x=>x.Photo.MediaId).Distinct().Count()!=samples.Count||
            samples.Any(x=>!Supported(x.Algorithm)||x.Analyte is not ("NO2" or "NO3" or "NH3")||
                x.Photo.MediaId==Guid.Empty||x.Photo.Bytes.Length is <1 or >5*1024*1024)||
            mode=="scheduled"&&samples.Count!=3)throw Invalid();
        var data=await Post("v1/batches/analyze",new{batch_id=batchId,mode,samples=samples.Select(s=>AnalyzePayload(area,s))},ct);
        try
        {
            var rows=data.GetProperty("results");
            if(data.GetProperty("batch_id").GetString()!=batchId||rows.ValueKind!=JsonValueKind.Array||
                rows.GetArrayLength()!=samples.Count)throw Invalid();
            var mapped=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
            int estimated=0;
            foreach(var r in rows.EnumerateArray())
            {
                var name=r.GetProperty("analyte").GetString();
                var sample=samples.SingleOrDefault(s=>s.Analyte==name);
                if(sample==null||r.GetProperty("media_id").GetString()!=sample.Photo.MediaId.ToString("D")||
                    !mapped.TryAdd(sample.Analyte,r))throw Invalid();
                var status=r.GetProperty("status").GetString();
                if(status=="estimated")estimated++;
                else if(status!="rejected")throw Invalid();
            }
            if(data.GetProperty("status").GetString()!=(estimated==samples.Count?"completed":estimated==0?"failed":"partial"))throw Invalid();
            var results=new List<BatchOutcome>();
            foreach(var s in samples)
            {
                var r=mapped[s.Analyte];
                if(r.GetProperty("status").GetString()=="rejected")
                {
                    // Do not relay arbitrary upstream messages, tokens or image contents to clients.
                    var error=r.TryGetProperty("error",out var code)&&code.ValueKind==JsonValueKind.String?code.GetString():null;
                    results.Add(new(s.Analyte,null,string.IsNullOrWhiteSpace(error)?502:error=="internal_error"?503:400));
                    continue;
                }
                try{results.Add(new(s.Analyte,ReadEstimate(r,s)));}
                catch(AppException ex) when(ex.StatusCode==502){results.Add(new(s.Analyte,null,502));}
            }
            return results;
        }
        catch(Exception ex) when(ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){throw Invalid();}
    }
    public record Control(string Status,decimal Estimated,decimal AbsoluteError,JsonElement Evidence);
    public async Task<Control> ValidateControl(Guid area,Guid camera,string analyte,string unit,string lot,string token,string calibrationId,Photo photo,decimal expected,decimal maxError,CancellationToken ct,string algorithm=Algorithm)
    {
        if(!Supported(algorithm))throw Invalid();
        var data=await Post("v1/calibrations/validate",new { sample=new { area_id=area.ToString("D"),camera_id=camera.ToString("D"),analyte,
            reagent_lot=lot,calibration_token=token,image_b64=Convert.ToBase64String(photo.Bytes),media_id=photo.MediaId.ToString("D"),
            reaction_seconds=photo.ReactionSeconds,salinity_ppt=photo.SalinityPpt },expected,max_error=maxError },ct);
        try
        {
            var status=data.GetProperty("status").GetString();
            var value=data.GetProperty("estimated").GetDecimal();var error=data.GetProperty("absolute_error").GetDecimal();
            var evidence=data.GetProperty("evidence");
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo.Bytes));
            if(status is not ("passed" or "failed")||data.GetProperty("algorithm").GetString()!=algorithm||data.GetProperty("calibration_id").GetString()!=calibrationId||
                data.GetProperty("analyte").GetString()!=analyte||data.GetProperty("unit").GetString()!=unit||
                data.GetProperty("media_id").GetString()!=photo.MediaId.ToString("D")||
                evidence.GetProperty("media_id").GetString()!=photo.MediaId.ToString("D")||!GeometryEvidence(evidence,algorithm)||
                !string.Equals(evidence.GetProperty("sha256").GetString(),hash,StringComparison.OrdinalIgnoreCase)||
                data.GetProperty("expected").GetDecimal()!=expected||data.GetProperty("max_error").GetDecimal()!=maxError||
                value<0||value>10000||error<0||Math.Abs(error-Math.Abs(value-expected))>0.000001m||
                (status=="passed")!=(error<=maxError))throw Invalid();
            return new(status,value,error,evidence.Clone());
        }
        catch(Exception ex) when(ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){throw Invalid();}
    }

    public async Task<JsonElement> InspectCalibrated(Guid area,Guid camera,string analyte,string lot,string token,string calibrationId,string algorithm,Photo photo,CancellationToken ct)
    {
        if(!Supported(algorithm))throw Invalid();
        var data=await Post("v1/calibrations/inspect",new { area_id=area.ToString("D"),camera_id=camera.ToString("D"),analyte,reagent_lot=lot,
            calibration_token=token,image_b64=Convert.ToBase64String(photo.Bytes),media_id=photo.MediaId.ToString("D"),
            reaction_seconds=photo.ReactionSeconds,salinity_ppt=photo.SalinityPpt },ct);
        try
        {
            var evidence=data.GetProperty("evidence");
            if(data.GetProperty("status").GetString()!="optical_roi_pass"||data.GetProperty("algorithm").GetString()!=algorithm||
               data.GetProperty("calibration_id").GetString()!=calibrationId||data.GetProperty("geometry_required").GetBoolean()!=(algorithm==GeometryAlgorithm)||
               evidence.GetProperty("media_id").GetString()!=photo.MediaId.ToString("D")||!GeometryEvidence(evidence,algorithm)||
               !string.Equals(evidence.GetProperty("sha256").GetString(),Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo.Bytes)),StringComparison.OrdinalIgnoreCase))
                throw Invalid();
            return data;
        }
        catch(Exception ex) when(ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){throw Invalid();}
    }
    public async Task<JsonElement> Inspect(ColorProtocol protocol,Photo photo,CancellationToken ct)
    {
        var data=await Post("v1/images/inspect",new { protocol=Protocol(protocol),photo=new { image_b64=Convert.ToBase64String(photo.Bytes),
            media_id=photo.MediaId.ToString("D"),reaction_seconds=photo.ReactionSeconds,salinity_ppt=photo.SalinityPpt } },ct);
        try
        {
            var evidence=data.GetProperty("evidence");
            if(data.GetProperty("status").GetString()!="optical_roi_pass"||evidence.GetProperty("media_id").GetString()!=photo.MediaId.ToString("D")||
                !string.Equals(evidence.GetProperty("sha256").GetString(),Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo.Bytes)),StringComparison.OrdinalIgnoreCase))
                throw Invalid();
            return data;
        }
        catch(Exception ex) when(ex is KeyNotFoundException or InvalidOperationException or FormatException){throw Invalid();}
    }
    public async Task<JsonElement> AmmoniaReference(string method,Guid assessmentId,Guid mediaId,decimal value,string unit,
        Nh3Environment env,CancellationToken ct)
    {
        WaterBenchAmmoniaPolicy.Method(method,unit,env);
        bool printed=method=="printed_table";
        object payload=printed?(object)new{sample_id=assessmentId.ToString("D"),media_id=mediaId.ToString("D"),value,input_basis="chart",
            ph=env.Ph,sample_at=env.SampleAt,ph_at=env.PhAt,temperature_c=env.TemperatureC,temperature_at=env.TemperatureAt,max_age_seconds=env.MaxAgeSeconds}:
            new{value,unit,ph=env.Ph,temperature_c=env.TemperatureC,salinity_ppt=env.SalinityPpt,sample_at=env.SampleAt,
                ph_at=env.PhAt,temperature_at=env.TemperatureAt,salinity_at=env.SalinityAt,max_age_seconds=env.MaxAgeSeconds};
        var data=await Post(printed?"v1/ammonia/thuy-phuoc/lookup":"v1/ammonia/convert",payload,ct);
        try
        {
            if(data.GetProperty("sample_at").GetDateTimeOffset()!=env.SampleAt)throw Invalid();
            var status=data.GetProperty("status").GetString();
            if(printed)
            {
                if(status is not ("reference_only" or "unavailable")||
                    data.GetProperty("revision").GetString()!="thuy-phuoc-photo-card-v1"||
                    data.GetProperty("method").GetString()!="printed_table_exact"||
                    data.GetProperty("sample_id").GetString()!=assessmentId.ToString("D")||
                    data.GetProperty("media_id").GetString()!=mediaId.ToString("D")||
                    data.GetProperty("input").GetDecimal()!=value||data.GetProperty("input_basis").GetString()!="chart"||
                    data.GetProperty("ph").GetDecimal()!=env.Ph||data.GetProperty("ph_at").GetDateTimeOffset()!=env.PhAt||
                    data.GetProperty("temperature_c").GetDecimal()!=env.TemperatureC||
                    data.GetProperty("temperature_at").GetDateTimeOffset()!=env.TemperatureAt||
                    data.GetProperty("chemical_accuracy_validated").GetBoolean()||data.GetProperty("crab_toxicity_threshold_validated").GetBoolean()||
                    data.GetProperty("temperature_salinity_corrected").GetBoolean())throw Invalid();
                var result=data.GetProperty("nh3_mg_l_printed");
                if(status=="reference_only"?(result.ValueKind!=JsonValueKind.Number||result.GetDecimal() is <0 or >5):
                    result.ValueKind!=JsonValueKind.Null)throw Invalid();
                return JsonSerializer.SerializeToElement(new{status,revision="thuy-phuoc-photo-card-v1",method="printed_table_exact",
                    nh3MgLPrinted=status=="reference_only"?result.GetDecimal():(decimal?)null,
                    reason=status=="unavailable"?data.GetProperty("reason").GetString():null,
                    temperatureSalinityCorrected=false},Json);
            }
            if(status!="estimated"||data.GetProperty("method").GetString()!="epa-1989-saltwater-whitfield-regression")throw Invalid();
            var free=data.GetProperty("free_nh3_mg_l").GetDecimal();
            var nitrogen=data.GetProperty("free_nh3_n_mg_l").GetDecimal();
            var tan=data.GetProperty("tan_n_mg_l").GetDecimal();
            var fraction=data.GetProperty("unionized_fraction").GetDecimal();
            if(free<0||nitrogen<0||tan<0||fraction is <=0 or >=1||Math.Abs(nitrogen-tan*fraction)>0.00001m||
                Math.Abs(free-nitrogen*(14.00674m+3*1.00794m)/14.00674m)>0.00001m||
                (unit=="mg/L TAN-N"?Math.Abs(tan-value):Math.Abs(tan*(1-fraction)*(14.00674m+4*1.00794m)/14.00674m-value))>0.00001m)
                throw Invalid();
            return JsonSerializer.SerializeToElement(new{status,method="epa-1989-saltwater-whitfield-regression",
                freeNh3MgL=free,freeNh3NMgL=nitrogen,tanNMgL=tan,unionizedFraction=fraction},Json);
        }
        catch(Exception ex) when(ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){throw Invalid();}
    }
    private static bool Hash(string? value)=>value is {Length:64}&&value.All(Uri.IsHexDigit);
    private static AppException Invalid()=>new("Dịch vụ AI nước trả dữ liệu không hợp lệ.",502);
    private async Task<JsonElement> Post(string path,object payload,CancellationToken ct)
    {
        var root=config["WaterAi:BaseUrl"];var key=config["WaterAi:ApiKey"];
        if(!Uri.TryCreate(root,UriKind.Absolute,out var url)||url.Scheme is not ("http" or "https")||(url.Scheme=="http"&&!url.IsLoopback)||
           !string.IsNullOrEmpty(url.UserInfo)||!string.IsNullOrEmpty(url.Query)||!string.IsNullOrEmpty(url.Fragment)||
           string.IsNullOrWhiteSpace(key)||key.Length<32)
            throw new AppException("Chưa cấu hình dịch vụ AI nước.",502);
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(url.AbsoluteUri.TrimEnd('/')+"/"+path));
        request.Headers.Add("X-AI-Key",key);
        request.Content=JsonContent.Create(payload,options:Json);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(response.StatusCode==HttpStatusCode.UnprocessableEntity)
                throw AppException.BadRequest("AI từ chối mẫu hoặc hiệu chuẩn: kiểm tra ảnh, thời gian, độ mặn, dải màu và mẫu đối chứng.");
            if(!response.IsSuccessStatusCode)
            {
                int status=(int)response.StatusCode;
                throw new AppException("Dịch vụ AI nước chưa sẵn sàng.",status is 408 or 429 or 500 or 502 or 503 or 504?503:502);
            }
            await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer=new MemoryStream();var chunk=new byte[8192];int n;
            while((n=await stream.ReadAsync(chunk.AsMemory(),timeout.Token))>0)
            {if(buffer.Length+n>512*1024)throw Invalid();await buffer.WriteAsync(chunk.AsMemory(0,n),timeout.Token);}
            using var document=JsonDocument.Parse(buffer.ToArray());return document.RootElement.Clone();
        }
        catch(HttpRequestException){throw new AppException("Không kết nối được dịch vụ AI nước.",503);}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new AppException("Dịch vụ AI nước quá thời gian xử lý.",503);}
        catch(JsonException){throw Invalid();}
    }
}
