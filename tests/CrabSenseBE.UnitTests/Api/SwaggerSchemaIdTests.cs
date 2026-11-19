using CrabSenseBE.Api;
using CrabSenseBE.Api.Controllers;
using FluentAssertions;

namespace CrabSenseBE.UnitTests.Api;

public sealed class SwaggerSchemaIdTests
{
    [Fact]
    public void NestedStartRequests_FromDifferentControllers_HaveDistinctSchemaIds()
    {
        var water = SwaggerSchemaIds.For(typeof(WaterAnalysisBatchController.StartRequest));
        var camera = SwaggerSchemaIds.For(typeof(CameraRecordingRuntimeController.StartRequest));

        water.Should().NotBe(camera);
        water.Should().Contain("WaterAnalysisBatchController.StartRequest");
        camera.Should().Contain("CameraRecordingRuntimeController.StartRequest");
    }

    [Fact]
    public void SchemaIds_AreValidAndDistinguishGenericArguments()
    {
        var first = SwaggerSchemaIds.For(typeof(Dictionary<string, int>));
        var second = SwaggerSchemaIds.For(typeof(Dictionary<string, Guid>));

        first.Should().MatchRegex("^[A-Za-z0-9._-]+$");
        second.Should().MatchRegex("^[A-Za-z0-9._-]+$");
        first.Should().NotBe(second);
    }
}
