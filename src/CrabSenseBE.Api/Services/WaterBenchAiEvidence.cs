using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.Api.Services;

// Server-generated provenance; a checksum is integrity checking, not an external signature.
public sealed record WaterBenchAiProvenance(string Contract,Guid AssessmentId,Guid RequestId,string RequestSha256,
    Guid StationId,Guid ConnectionId,string BootId,string BatchId,string SessionId,string Analyte,int CaptureId,
    Guid SessionReceiptId,string SessionPayloadSha256,Guid ImageReceiptId,string ImagePayloadSha256,
    Guid MediaId,string ImageSha256,Guid CalibrationId,Guid CameraId,int ConfigurationVersion,
    string AiCalibrationId,string Algorithm);

public static class WaterBenchAiEvidence
{
    public const string Contract="water-bench-ai-evidence-v1";
    public static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void Require(bool ok)
    {if(!ok)throw AppException.Conflict("Water bench AI evidence linkage or digest mismatch.");}
    public static WaterBenchAiProvenance Build(WaterBenchResultAssessment row,WaterBenchEvidence session,WaterBenchEvidence image,
        string aiCalibrationId,string algorithm)
    {
        _=WaterBenchReceiptPolicy.Read(session);
        var receipt=WaterBenchResultPolicy.VerifiedReceipt(image);
        Require(session.Kind=="session"&&session.Id==row.SessionEvidenceId&&image.Id==row.ImageEvidenceId&&
            session.StationId==image.StationId&&session.ConnectionId==image.ConnectionId&&session.BootId==image.BootId&&
            session.FirmwareBatchId==image.FirmwareBatchId&&session.FirmwareSessionId==image.FirmwareSessionId&&
            session.Analyte==image.Analyte&&session.FirmwareSessionId!=null&&image.CaptureId is >=1 and <=65536&&
            receipt.MediaId==row.MediaId&&receipt.CalibrationId==row.CalibrationId&&receipt.CalibrationId is Guid cal&&cal!=Guid.Empty&&
            receipt.CameraId is Guid camera&&camera!=Guid.Empty&&receipt.ConfigurationVersion is >0&&
            row.Id!=Guid.Empty&&row.RequestId!=Guid.Empty&&WaterBenchReceiptPolicy.IsDigest(row.RequestHash)&&
            WaterBenchReceiptPolicy.IsDigest(aiCalibrationId)&&WaterAiClient.Supported(algorithm));
        return new(Contract,row.Id,row.RequestId,row.RequestHash,session.StationId,session.ConnectionId,session.BootId!,
            session.FirmwareBatchId!,session.FirmwareSessionId!,session.Analyte!,image.CaptureId!.Value,
            session.Id,session.PayloadSha256,image.Id,image.PayloadSha256,receipt.MediaId,receipt.Sha256,
            receipt.CalibrationId!.Value,receipt.CameraId!.Value,receipt.ConfigurationVersion!.Value,aiCalibrationId,algorithm);
    }
    public static string Digest(WaterBenchAiProvenance value)=>Hash(JsonSerializer.Serialize(value,WaterBatchPlanning.Json));
    // Legacy rows retain their original status: do not silently manufacture an inference-time snapshot.
    public static WaterBenchAiProvenance? Verify(WaterBenchResultAssessment row,WaterBenchEvidence session,WaterBenchEvidence image)
    {
        Require(row.Status=="estimate_review_required"&&row.CandidateValue is >=0 and <=10000&&row.HoldoutError is >=0 and <=1000&&
            WaterBenchResultPolicy.ValidUnit(session.Analyte!,row.Unit??"")&&Encoding.UTF8.GetByteCount(row.EvidenceJson)<=65536);
        try
        {
            using var doc=JsonDocument.Parse(row.EvidenceJson);var root=doc.RootElement;
            var reading=root.GetProperty("reading");var algo=root.GetProperty("algorithm").GetString();
            var aiId=root.GetProperty("aiCalibrationId").GetString();
            var expected=Build(row,session,image,aiId!,algo!);
            Require(root.GetProperty("imageSha256").GetString()==expected.ImageSha256&&
                reading.GetProperty("media_id").GetString()==expected.MediaId.ToString("D")&&
                string.Equals(reading.GetProperty("sha256").GetString(),expected.ImageSha256,StringComparison.OrdinalIgnoreCase));
            if(!root.TryGetProperty("provenance",out var p))
            {Require(!root.TryGetProperty("provenanceSha256",out _));return null;}
            var stored=p.Deserialize<WaterBenchAiProvenance>(WaterBatchPlanning.Json);
            Require(stored==expected&&root.GetProperty("provenanceSha256").GetString()==Digest(expected));
            // The snapshot additionally binds the numeric result and the exact optical evidence.
            Require(root.GetProperty("candidateValue").GetDecimal()==row.CandidateValue&&
                root.GetProperty("unit").GetString()==row.Unit&&root.GetProperty("holdoutError").GetDecimal()==row.HoldoutError&&
                root.GetProperty("readingSha256").GetString()==Hash(reading.GetRawText()));
            return expected;
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or OverflowException)
        {throw AppException.Conflict("Invalid stored AI evidence.");}
    }
}
