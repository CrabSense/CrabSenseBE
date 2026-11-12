using CrabSenseBE.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.UnitTests.Api;

public sealed class SyncStubSafetyTests
{
    [Fact]
    public void LegacyQueueEndpoints_DoNotReturnFakeSuccess()
    {
        var syncQueue = new SyncQueueStubController(
            null!, null!, null!, null!);
        var synchronization = new SynchronizationV1StubController(null!);

        AssertNotImplemented(syncQueue.EnqueueSync(new { value = 1 }));
        AssertNotImplemented(synchronization.Enqueue(new { value = 1 }));
    }

    [Fact]
    public void UntypedDownload_DoesNotReturnFakeUrl()
    {
        var controller = new SyncV1StubController(null!);

        AssertNotImplemented(controller.Download(
            Guid.NewGuid(), "artifact", "unknown"));
    }

    private static void AssertNotImplemented(IActionResult result)
    {
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(
            StatusCodes.Status501NotImplemented);
    }
}
