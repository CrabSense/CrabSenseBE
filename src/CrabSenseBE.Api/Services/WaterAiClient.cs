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
    public record Photo(Guid MediaId, byte[] Bytes, int ReactionSeconds, decimal SalinityPpt, decimal? Concentration = null);
    public record Fitted(string Token, string Id, string Algorithm, JsonElement Curve, JsonElement Validation, DateTime ValidUntil);
    public record Estimate(decimal Value, JsonElement Evidence, decimal HoldoutError);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static object Roi(ColorRoi r) => new { x=r.X,y=r.Y,width=r.Width,height=r.Height };
    private static object Protocol(ColorProtocol p) => new { width=p.Width,height=p.Height,sample=Roi(p.Sample),reference=Roi(p.Reference),
        reaction_seconds=p.ReactionSeconds,tolerance_seconds=p.TimingToleranceSeconds,sample_volume_ml=p.SampleVolumeMl,
        min_salinity_ppt=p.MinSalinityPpt,max_salinity_ppt=p.MaxSalinityPpt };
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
            if(string.IsNullOrWhiteSpace(token)||token.Length>100000||!Hash(id)||algo!=Algorithm||
                data.GetProperty("unit").GetString()!=unit||Math.Abs((expiry-validUntil).TotalSeconds)>1||
                curve.ValueKind!=JsonValueKind.Object||curve.GetProperty("channel").GetInt32() is <0 or >2||checks.ValueKind!=JsonValueKind.Array||checks.GetArrayLength()!=1||
                checks[0].GetProperty("media_id").GetString()!=validation.MediaId.ToString("D")||
                checks[0].GetProperty("absolute_error").GetDecimal()<0||checks[0].GetProperty("absolute_error").GetDecimal()>tolerance)
                throw Invalid();
            return new(token,id!,algo,curve.Clone(),checks.Clone(),expiry);
        }
        catch(Exception ex) when(ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){throw Invalid();}
    }
    public async Task<Estimate> Analyze(Guid area, Guid camera, string analyte,string unit,string lot,string token,string calibrationId,Photo photo,CancellationToken ct)
    {
        var data=await Post("v1/analyze",new { area_id=area.ToString("D"),camera_id=camera.ToString("D"),analyte,reagent_lot=lot,
            calibration_token=token,image_b64=Convert.ToBase64String(photo.Bytes),media_id=photo.MediaId.ToString("D"),
            reaction_seconds=photo.ReactionSeconds,salinity_ppt=photo.SalinityPpt },ct);
        try
        {
            var value=data.GetProperty("value").GetDecimal();var error=data.GetProperty("holdout_max_error").GetDecimal();
            var evidence=data.GetProperty("evidence");
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo.Bytes));
            if(data.GetProperty("status").GetString()!="estimated"||data.GetProperty("analyte").GetString()!=analyte||
                data.GetProperty("unit").GetString()!=unit||data.GetProperty("algorithm").GetString()!=Algorithm||
                data.GetProperty("calibration_id").GetString()!=calibrationId||data.GetProperty("media_id").GetString()!=photo.MediaId.ToString("D")||
                value<0||value>10000||error<0||error>1000||data.GetProperty("confidence").ValueKind!=JsonValueKind.Null||
                evidence.GetProperty("media_id").GetString()!=photo.MediaId.ToString("D")||
                !string.Equals(evidence.GetProperty("sha256").GetString(),hash,StringComparison.OrdinalIgnoreCase))throw Invalid();
            return new(value,evidence.Clone(),error);
        }
        catch(Exception ex) when(ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){throw Invalid();}
    }
    public record Control(string Status,decimal Estimated,decimal AbsoluteError,JsonElement Evidence);
    public async Task<Control> ValidateControl(Guid area,Guid camera,string analyte,string unit,string lot,string token,string calibrationId,Photo photo,decimal expected,decimal maxError,CancellationToken ct)
    {
        var data=await Post("v1/calibrations/validate",new { sample=new { area_id=area.ToString("D"),camera_id=camera.ToString("D"),analyte,
            reagent_lot=lot,calibration_token=token,image_b64=Convert.ToBase64String(photo.Bytes),media_id=photo.MediaId.ToString("D"),
            reaction_seconds=photo.ReactionSeconds,salinity_ppt=photo.SalinityPpt },expected,max_error=maxError },ct);
        try
        {
            var status=data.GetProperty("status").GetString();
            var value=data.GetProperty("estimated").GetDecimal();var error=data.GetProperty("absolute_error").GetDecimal();
            var evidence=data.GetProperty("evidence");
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo.Bytes));
            if(status is not ("passed" or "failed")||data.GetProperty("calibration_id").GetString()!=calibrationId||
                data.GetProperty("analyte").GetString()!=analyte||data.GetProperty("unit").GetString()!=unit||
                data.GetProperty("media_id").GetString()!=photo.MediaId.ToString("D")||
                evidence.GetProperty("media_id").GetString()!=photo.MediaId.ToString("D")||
                !string.Equals(evidence.GetProperty("sha256").GetString(),hash,StringComparison.OrdinalIgnoreCase)||
                data.GetProperty("expected").GetDecimal()!=expected||data.GetProperty("max_error").GetDecimal()!=maxError||
                value<0||value>10000||error<0||Math.Abs(error-Math.Abs(value-expected))>0.000001m||
                (status=="passed")!=(error<=maxError))throw Invalid();
            return new(status,value,error,evidence.Clone());
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
    private static bool Hash(string? value)=>value is {Length:64}&&value.All(Uri.IsHexDigit);
    private static AppException Invalid()=>new("Dịch vụ AI nước trả dữ liệu không hợp lệ.",502);
    private async Task<JsonElement> Post(string path,object payload,CancellationToken ct)
    {
        var root=config["WaterAi:BaseUrl"];var key=config["WaterAi:ApiKey"];
        if(!Uri.TryCreate(root,UriKind.Absolute,out var url)||url.Scheme is not ("http" or "https")||(url.Scheme=="http"&&!url.IsLoopback)||
           !string.IsNullOrEmpty(url.UserInfo)||!string.IsNullOrEmpty(url.Query)||!string.IsNullOrEmpty(url.Fragment)||
           string.IsNullOrWhiteSpace(key)||key.Length<32)
            throw new AppException("Chưa cấu hình dịch vụ AI nước.",503);
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(url.AbsoluteUri.TrimEnd('/')+"/"+path));
        request.Headers.Add("X-AI-Key",key);
        request.Content=JsonContent.Create(payload,options:Json);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(response.StatusCode==HttpStatusCode.UnprocessableEntity)
                throw AppException.BadRequest("AI từ chối mẫu hoặc hiệu chuẩn: kiểm tra ảnh, thời gian, độ mặn, dải màu và mẫu đối chứng.");
            if(!response.IsSuccessStatusCode)throw new AppException("Dịch vụ AI nước chưa sẵn sàng.",503);
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
