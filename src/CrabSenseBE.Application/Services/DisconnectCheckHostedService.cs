using CrabSenseBE.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Periodic scan for sensor/device (camera) disconnect → create alerts + notify operators.
/// </summary>
public class DisconnectCheckHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DisconnectCheckHostedService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);
    private readonly int _timeoutMinutes = 15;

    public DisconnectCheckHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<DisconnectCheckHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // delay first run so app finishes boot / migrate
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();
                var result = await alerts.CheckDisconnectsAsync(_timeoutMinutes, stoppingToken);
                _logger.LogInformation("Disconnect check: {Message}", result.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Disconnect check failed");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
