using CrabSenseBE.Domain.Entities;
using Xunit;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class AiRuntimeStateTests
{
    [Fact]
    public void RegistrationAndRequestedStatusAreNotOnline()
    {
        var now = DateTime.UtcNow;
        var service = new AiManagedService { Status = "RestartRequested", Version = "v1" };
        Assert.Equal("NotIntegrated", AiRuntimeState.Health(service, now));
        service.LastHeartbeatAt = now;
        service.ReportedState = "Stopped";
        Assert.Equal("Stopped", AiRuntimeState.Health(service, now));
        service.ReportedState = "Running";
        service.ReportedVersion = "v2";
        Assert.Equal("Degraded", AiRuntimeState.Health(service, now));
        service.ReportedVersion = "v1";
        Assert.Equal("Degraded", AiRuntimeState.Health(service, now));
        service.StartedAt = now;
        Assert.Equal("Online", AiRuntimeState.Health(service, now));
        Assert.Equal("Offline", AiRuntimeState.Health(service, now.AddSeconds(91)));
        service.IsEnabled = false;
        Assert.Equal("Disabled", AiRuntimeState.Health(service, now));
    }
}
