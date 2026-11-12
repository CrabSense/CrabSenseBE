using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Moq;
using FluentAssertions;

namespace CrabSenseBE.UnitTests.Application;

public sealed class HardwareControlDisabledTests
{
    [Fact]
    public async Task Disabled_mode_blocks_manual_automation_poll_and_ack_without_database()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["EdgeSync:ControlDisabled"] = "true" }).Build();
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var service = new EdgeCommandService(uow.Object, configuration: config);
        var command = new EnqueueEdgeCommandRequest("ESP-A", "on", "1");
        var actor = Guid.NewGuid();
        Func<Task>[] calls =
        [
            () => service.EnqueueManualAsync(command, actor),
            () => service.EnqueueAutomationAsync(command, actor, "test"),
            () => service.GetPendingAsync("ESP-A", "device:test"),
            () => service.AcknowledgeAsync(Guid.NewGuid(), new EdgeCommandAckRequest("acknowledged"), "ESP-A")
        ];
        foreach (var call in calls)
            (await call.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(503);
        uow.VerifyNoOtherCalls();
    }

    [Fact]
    public void Production_default_configuration_disables_control()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CrabSenseBE.sln"))) dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "src", "CrabSenseBE.Api", "appsettings.json");
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        json.RootElement.GetProperty("EdgeSync").GetProperty("ControlDisabled").GetBoolean().Should().BeTrue();
    }
}
