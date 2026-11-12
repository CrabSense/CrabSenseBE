using FluentAssertions;

namespace CrabSenseBE.UnitTests.Api;

public sealed class DeploymentSafetyTests
{
    [Fact]
    public void ProductionDeploy_IsGatedByCi_AndDoesNotEnableDevelopment()
    {
        var root = RepositoryRoot();
        var workflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "deploy-vps.yml"));

        workflow.Should().Contain("workflow_run:");
        workflow.Should().Contain("github.event.workflow_run.conclusion == 'success'");
        workflow.Should().Contain("ASPNETCORE_ENVIRONMENT=Production");
        workflow.Should().NotContain("ASPNETCORE_ENVIRONMENT=Development");
        workflow.Should().Contain("pg_dump");
        workflow.Should().Contain("migrations bundle");
        workflow.Should().Contain("restoring previous application package");
        workflow.Should().NotContain("vars.EDGE_PROTOCOL_V2_READY == 'true'");
        workflow.Should().Contain("Environment=EdgeSync__ControlDisabled=true");
        workflow.Should().Contain("Environment=RasAutomation__FirmwareInterlockVerified=false");
        workflow.Should().Contain("/health/edge-control");
        workflow.Should().Contain("/opt/crabsense-api.previous/.hardware-control-disabled");
        workflow.Should().Contain("/opt/crabsense-api.previous/.edge-protocol-v2");
        workflow.Should().Contain("Protocol-1 application rollback is unsafe");
    }

    [Fact]
    public void ProductionDeploy_UsesExactMain_PersistentMedia_AndSchemaMatchedRollback()
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "deploy-vps.yml"));
        workflow.Should().Contain("run.event === 'push' && run.head_branch === 'main'");
        workflow.Should().Contain("Refusing stale or non-main deployment");
        workflow.Should().Contain("/var/lib/crabsense/media-storage");
        workflow.Should().Contain("cmp -s /opt/crabsense-api.previous/.schema-migration");
        workflow.Should().Contain("MIGRATION_STARTED=true");
        workflow.IndexOf("systemctl stop crabsense-api", StringComparison.Ordinal)
            .Should().BeLessThan(workflow.IndexOf("backup = sys.argv[1]", StringComparison.Ordinal));
        workflow.Should().NotContain("./efbundle --connection");
    }

    [Fact]
    public void DevBootstrap_RemainsDevelopmentOnly()
    {
        var root = RepositoryRoot();
        var program = File.ReadAllText(
            Path.Combine(root, "src", "CrabSenseBE.Api", "Program.cs"));

        program.Should().Contain("if (app.Environment.IsDevelopment())");
        program.Should().Contain("DevDbBootstrap.InitializeAsync(app)");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CrabSenseBE.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new DirectoryNotFoundException("CrabSenseBE repository root not found.");
    }
}
