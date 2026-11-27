using System.Text.Json;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBenchAiEvidenceTests
{
    [Fact]
    public void SnapshotBindsSessionImageCalibrationAndResultWithoutBackfillingLegacy()
    {
        var session=new WaterBenchEvidence{StationId=Guid.NewGuid(),EventId=Guid.NewGuid(),ConnectionId=Guid.NewGuid(),
            BootId="0123456789abcdef",FirmwareBatchId="abcdef0123456789",FirmwareSessionId="abcdef0123456789-1",
            Kind="session",Analyte="NO2",KioskReceivedAt=DateTime.UtcNow};
        void Set(WaterBenchEvidence e,object payload)
        {
            e.PayloadJson=JsonSerializer.Serialize(payload,new JsonSerializerOptions(JsonSerializerDefaults.Web));
            e.PayloadSha256=WaterBenchAiEvidence.Hash(e.PayloadJson);
        }
        Set(session,new BenchBatchEvent(session.StationId,session.EventId,session.ConnectionId,session.BootId!,session.FirmwareBatchId!,
            2,"session",DateTimeOffset.UtcNow,Analyte:"NO2",SessionId:session.FirmwareSessionId));
        var image=new WaterBenchEvidence{StationId=session.StationId,EventId=Guid.NewGuid(),ConnectionId=session.ConnectionId,
            BootId=session.BootId,FirmwareBatchId=session.FirmwareBatchId,FirmwareSessionId=session.FirmwareSessionId,
            Kind="jpeg",Analyte="NO2",CaptureId=1,KioskReceivedAt=DateTime.UtcNow};
        var media=Guid.NewGuid();var cal=Guid.NewGuid();var camera=Guid.NewGuid();var hash=new string('a',64);
        Set(image,new{mediaId=media,sha256=hash,contract="water-bench-media-v1",calibrationId=cal,cameraId=camera,configurationVersion=1});
        var row=new WaterBenchResultAssessment{SessionEvidenceId=session.Id,ImageEvidenceId=image.Id,MediaId=media,CalibrationId=cal,
            RequestId=Guid.NewGuid(),RequestHash=new string('b',64),Status="estimate_review_required",CandidateValue=1m,Unit="mg/L NO2",HoldoutError=0.1m};
        var aiId=new string('c',64);var algo=WaterAiClient.Algorithm;
        var reading=new{media_id=media.ToString("D"),sha256=hash};
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        row.EvidenceJson=JsonSerializer.Serialize(new{algorithm=algo,aiCalibrationId=aiId,imageSha256=hash,reading},options);
        Assert.Null(WaterBenchAiEvidence.Verify(row,session,image));
        var provenance=WaterBenchAiEvidence.Build(row,session,image,aiId,algo);
        row.EvidenceJson=JsonSerializer.Serialize(new{algorithm=algo,aiCalibrationId=aiId,imageSha256=hash,reading,provenance,
            provenanceSha256=WaterBenchAiEvidence.Digest(provenance),candidateValue=row.CandidateValue,unit=row.Unit,holdoutError=row.HoldoutError,
            readingSha256=WaterBenchAiEvidence.Hash(JsonSerializer.Serialize(reading,options))},options);
        Assert.Equal(provenance,WaterBenchAiEvidence.Verify(row,session,image));
        row.CandidateValue=2m;
        Assert.Throws<AppException>(()=>WaterBenchAiEvidence.Verify(row,session,image));
        row.CandidateValue=1m;image.ConnectionId=Guid.NewGuid();
        Assert.Throws<AppException>(()=>WaterBenchAiEvidence.Verify(row,session,image));
    }
}
