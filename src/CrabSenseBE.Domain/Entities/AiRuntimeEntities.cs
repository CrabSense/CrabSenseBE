using CrabSenseBE.Domain.Common;
namespace CrabSenseBE.Domain.Entities;

public class AiModelVersion : BaseEntity
{
    public Guid AiServiceId { get; set; }
    public string Version { get; set; } = "";
    public string ArtifactName { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
public class AiCameraAssignment : BaseEntity
{
    public Guid AiServiceId { get; set; }
    public Guid DeviceId { get; set; }
}
public class AiInferenceJob : BaseEntity
{
    public Guid AiServiceId { get; set; }
    public Guid ModelVersionId { get; set; }
    public Guid MediaId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid BoxId { get; set; }
    public Guid? CrabId { get; set; }
    public Guid FarmingAreaId { get; set; }
    public string ModelVersion { get; set; } = "";
    public decimal ConfidenceThreshold { get; set; }
    public decimal FrameSamplingFps { get; set; }
    public string DetectionClassesJson { get; set; } = "[]";
    public string Status { get; set; } = "Queued";
    public int Attempts { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public long? DurationMs { get; set; }
}
