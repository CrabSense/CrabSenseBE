using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrabSenseBE.Application.Services;

/// <summary>Advances simulated runs and records hardware deadlines without client polling.
/// Never sends physical commands. Each area uses a fresh unit of work.</summary>
public sealed class WaterAnalysisProgressHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<WaterAnalysisProgressHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                using var scan = scopeFactory.CreateScope();
                if (scan.ServiceProvider.GetRequiredService<IDatabaseBackupService>().IsMaintenanceMode)
                    continue;
                var uow = scan.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var active = await uow.WaterAnalysisRuns.FindAsync(
                    r => r.Status == "running" || r.Status == "stopping", stoppingToken);
                foreach (var areaId in active.Select(r => r.FarmingAreaId).Distinct())
                {
                    using var scope = scopeFactory.CreateScope();
                    if (scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>().IsMaintenanceMode)
                        break;
                    try
                    {
                        var service = (WaterAnalysisService)scope.ServiceProvider.GetRequiredService<IWaterAnalysisService>();
                        await service.AdvanceActiveAsync(areaId, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Water analysis transition failed for area {AreaId}", areaId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Water analysis progress scan failed");
            }
        }
    }
}
