using CrabSenseBE.Application.Services;
using Xunit;

namespace CrabSenseBE.UnitTests.Application;

public sealed class SyncMutationPolicyTests
{
    [Fact]
    public void Stale_missing_and_future_client_times_are_rejected()
    {
        var server = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(server.AddHours(1));
        Assert.True(SyncMutationPolicy.IsStale(server, null, null, now));
        Assert.True(SyncMutationPolicy.IsStale(server, new DateTimeOffset(server), null, now));
        Assert.True(SyncMutationPolicy.IsStale(server, now.AddMinutes(6), null, now));
        Assert.True(SyncMutationPolicy.IsStale(server, now, new DateTimeOffset(server.AddSeconds(-1)), now));
        Assert.False(SyncMutationPolicy.IsStale(server, now, new DateTimeOffset(server), now));
        Assert.False(SyncMutationPolicy.IsStale(server, now, null, now));
    }

    [Theory]
    [InlineData("empty", true, false)]
    [InlineData("active", false, false)]
    [InlineData("molting", false, false)]
    [InlineData("harvested", true, false)]
    [InlineData("active", true, true)]
    [InlineData("empty", false, true)]
    [InlineData("watch", true, true)]
    [InlineData("quarantine", true, false)]
    [InlineData("quarantine", false, false)]
    public void Box_status_must_match_occupancy(string status, bool occupied, bool allowed)
        => Assert.Equal(allowed, SyncMutationPolicy.IsBoxStatusConsistent(status, occupied));
}
