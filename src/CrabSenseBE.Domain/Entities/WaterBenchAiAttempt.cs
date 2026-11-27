using CrabSenseBE.Domain.Common;
namespace CrabSenseBE.Domain.Entities;

// Durable inference attempt, independent of hardware commands and final assessments.
public sealed class WaterBenchAiAttempt : BaseEntity
{
    public Guid SessionEvidenceId { get; set; }
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = "";
    public string? RequestJson { get; set; }
    public int Number { get; set; }
    public string State { get; set; } = "running";
    public DateTime LeaseUntil { get; set; }
    public DateTime? RetryAfter { get; set; }
}
