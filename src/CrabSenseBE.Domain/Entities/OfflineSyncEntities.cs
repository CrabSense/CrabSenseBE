namespace CrabSenseBE.Domain.Entities;

/// <summary>
/// Durable inbox for mobile mutations. Domain handlers can process the
/// payload after it has been accepted without losing it on app restart.
/// </summary>
public sealed class SyncInboxItem
{
    public Guid Id { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string EntityId { get; set; } = null!;
    public string OperationType { get; set; } = null!;
    public int? BaseVersion { get; set; }
    public DateTimeOffset? ClientUpdatedAt { get; set; }
    public string PayloadJson { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
