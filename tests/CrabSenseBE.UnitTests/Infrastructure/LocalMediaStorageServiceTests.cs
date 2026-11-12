using CrabSenseBE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class LocalMediaStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"crabsense-media-{Guid.NewGuid():N}");

    [Fact]
    public async Task StorageKey_CannotEscapeConfiguredRoot()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MediaStorage:LocalRoot"] = _root
            })
            .Build();
        var service = new LocalMediaStorageService(
            configuration,
            NullLogger<LocalMediaStorageService>.Instance);

        var act = () => service.DownloadAsync("../secret.txt");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
