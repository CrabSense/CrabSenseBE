using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Quarantined assessment; never a published WaterMeasurement or WaterAnalysisRun.</summary>
public sealed class WaterBenchResultAssessment : BaseEntity
{
    public Guid SessionEvidenceId { get; set; }
    public Guid RequestId { get; set; }
    public Guid AssessedBy { get; set; }
    public string Status { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid? ImageEvidenceId { get; set; }
    public Guid? MediaId { get; set; }
    public Guid? CalibrationId { get; set; }
    public decimal? CandidateValue { get; set; }
    public string? Unit { get; set; }
    public decimal? HoldoutError { get; set; }
    public string EvidenceJson { get; set; } = "{}";
}
