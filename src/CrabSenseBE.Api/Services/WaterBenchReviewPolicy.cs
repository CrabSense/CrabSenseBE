using System.Text;
using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Api.Controllers;
namespace CrabSenseBE.Api.Services;
public static class WaterBenchReviewPolicy
{
    public static void Validate(WaterBenchResultController.ReviewRequest r,string analyte)
    {
        if(r.RequestId==Guid.Empty||r.Decision is not ("approved" or "rejected")||string.IsNullOrWhiteSpace(r.Reason)||r.Reason.Length>500)
            throw AppException.BadRequest("Review decision and reason required.");
        if(r.Decision=="approved")
        {
            if(r.ReferenceMediaId is null||r.ReferenceMediaId==Guid.Empty||r.ReferenceValue is null or <0 or >10000||
                !WaterBenchResultPolicy.ValidUnit(analyte,r.ReferenceUnit??"")||string.IsNullOrWhiteSpace(r.ReferenceMethod)||
                r.ReferenceMethod.Length>250||!r.SameSampleConfirmed)throw AppException.BadRequest("Independent same-sample reference and exact unit required.");
        }
        else if(r.ReferenceMediaId!=null||r.ReferenceValue!=null||r.ReferenceUnit!=null||r.ReferenceMethod!=null||r.SameSampleConfirmed)
            throw AppException.BadRequest("Rejection requires reason only.");
    }
    public static JsonElement? Read(string json,string key)
    {
        using var doc=JsonDocument.Parse(json);
        var root=doc.RootElement;
        if(!root.TryGetProperty(key,out var value))
        {
            if(root.TryGetProperty(key+"Sha256",out _))throw AppException.Conflict("Orphan review checksum.");
            return null;
        }
        if(!root.TryGetProperty(key+"Sha256",out var digest)||digest.GetString()!=WaterBenchAiEvidence.Hash(value.GetRawText()))
            throw AppException.Conflict("Review checksum mismatch.");
        return value.Clone();
    }
    public static JsonElement? Replay(string json,string key,Guid request,string hash)
    {
        var existing=Read(json,key);
        if(existing==null)return null;
        if(existing.Value.GetProperty("requestId").GetGuid()!=request||existing.Value.GetProperty("requestSha256").GetString()!=hash)
            throw AppException.Conflict("Decision already recorded; use original request.");
        return existing;
    }
    public static string Append(string json,string key,JsonElement value)
    {
        if(key is not ("review" or "publication")||Read(json,key)!=null)throw AppException.Conflict("Immutable decision.");
        string raw=value.GetRawText();string original=json.TrimEnd();
        if(!original.EndsWith("}",StringComparison.Ordinal))throw AppException.Conflict("Invalid evidence.");
        string next=original[..^1]+(original=="{}"?"":",")+"\""+key+"\":"+raw+",\""+key+"Sha256\":\""+WaterBenchAiEvidence.Hash(raw)+"\"}";
        if(Encoding.UTF8.GetByteCount(next)>65536)throw AppException.Conflict("Evidence capacity exceeded.");
        return next;
    }
}
