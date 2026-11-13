namespace CrabSenseBE.Application.Interfaces;

public sealed record PortCommandDecision(long Revision, bool Allowed);

public interface IControllerPortPolicy
{
    // Null means a legacy controller has not reported an inventory.
    Task<PortCommandDecision?> CheckAsync(Guid deviceId, string? channel, string command, CancellationToken ct);
}
