using System.Reflection;
using CrabSenseBE.Api;
using CrabSenseBE.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.OpenApi.Models;

namespace CrabSenseBE.UnitTests.Api;

public sealed class SwaggerGroupingTests
{
    [Theory]
    [InlineData("/api/auth/login", "00. Authentication")]
    [InlineData("/api/farming-rows", "11. Farm - Farming Rows")]
    [InlineData("/api/boxes/{boxId:guid}/qr", "15. Farm - QR & Traceability")]
    [InlineData("/api/boxes/{boxId:guid}/moltings", "14. Farm - Operations & Inspections")]
    [InlineData("/api/frozen-lots/{frozenLotId:guid}/qr", "15. Farm - QR & Traceability")]
    [InlineData("/api/harvest-assignments", "16. Farm - Harvest & Frozen Storage")]
    [InlineData("/api/harvest-assignments/{id:guid}/complete", "16. Farm - Harvest & Frozen Storage")]
    [InlineData("/api/areas/{areaId:guid}/ras-flow", "23. IoT - RAS & Water Systems")]
    [InlineData("/api/areas/{areaId:guid}/water-analysis/batches", "24. Water - Chemical Analysis")]
    [InlineData("/api/iot/sensor-tests/pending", "20. IoT - Sensors")]
    [InlineData("/api/iot/controllers/capabilities", "21. IoT - Controllers & Devices")]
    [InlineData("/api/camera-schedules", "31. Cameras - Recording Schedules")]
    [InlineData("/api/cameras/{deviceId:guid}/recording-runtime/executions", "32. Cameras - Recordings & Runtime")]
    [InlineData("/api/ai/services/{id:guid}/models", "40. AI - Services & Runtime")]
    [InlineData("/api/ai/inference/jobs", "41. AI - Inference & Training")]
    [InlineData("/api/system/device-monitoring/summary", "50. Monitoring - Device Health")]
    [InlineData("/api/system/alerts", "51. Monitoring - Alerts & Thresholds")]
    [InlineData("/api/v1/notifications/register", "52. Monitoring - Notifications")]
    [InlineData("/api/admin/db/backups", "81. System - Backups & Storage")]
    public void GroupForRoute_GroupsByBusinessCapability(string route, string expected) =>
        SwaggerTagOrderDocumentFilter.GroupForRoute(route).Should().Be(expected);

    [Fact]
    public void Rewrite_ReplacesLegacyTags_AndOrdersOnlyActiveGroups()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var camera = new OpenApiPathItem();
        camera.Operations.Add(OperationType.Get, new OpenApiOperation
        {
            Tags = [new OpenApiTag { Name = "CameraRecordingRuntime" }]
        });
        document.Paths.Add("/api/cameras/recordings", camera);

        var auth = new OpenApiPathItem();
        auth.Operations.Add(OperationType.Post, new OpenApiOperation
        {
            Tags = [new OpenApiTag { Name = "00. Auth" }, new OpenApiTag { Name = "AuthController" }]
        });
        document.Paths.Add("/api/auth/login", auth);

        SwaggerTagOrderDocumentFilter.Rewrite(document);

        document.Tags.Select(tag => tag.Name).Should().Equal(
            "00. Authentication", "32. Cameras - Recordings & Runtime");
        auth.Operations[OperationType.Post].Tags.Select(tag => tag.Name)
            .Should().Equal("00. Authentication");
        camera.Operations[OperationType.Get].Tags.Select(tag => tag.Name)
            .Should().Equal("32. Cameras - Recordings & Runtime");
    }

    [Fact]
    public void EveryHttpAction_HasAnExplicitBusinessGroup()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type));
        var total = 0;

        foreach (var controller in controllers)
        {
            var prefixes = controller.GetCustomAttributes<RouteAttribute>(true)
                .Select(attribute => attribute.Template ?? string.Empty)
                .DefaultIfEmpty(string.Empty).ToArray();

            foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            foreach (var attribute in method.GetCustomAttributes<HttpMethodAttribute>(true))
            foreach (var prefix in prefixes)
            {
                var template = attribute.Template ?? string.Empty;
                var path = template.StartsWith("/", StringComparison.Ordinal) || template.StartsWith("~/", StringComparison.Ordinal)
                    ? template.TrimStart('~', '/')
                    : string.IsNullOrWhiteSpace(prefix)
                        ? template
                        : prefix.TrimEnd('/') + "/" + template.TrimStart('/');

                SwaggerTagOrderDocumentFilter.GroupForRoute(path)
                    .Should().NotBe(SwaggerTagOrderDocumentFilter.UnclassifiedGroup,
                        $"action {controller.Name}.{method.Name} has path {path}");
                total++;
            }
        }

        total.Should().BeGreaterThan(200);
    }
}
