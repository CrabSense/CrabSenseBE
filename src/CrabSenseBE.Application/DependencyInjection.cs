using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Options;
using CrabSenseBE.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrabSenseBE.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FcmOptions>(configuration.GetSection(FcmOptions.SectionName));
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IFarmingService, FarmingService>();
        services.AddScoped<IFarmLotService, FarmLotService>();
        services.AddScoped<IHarvestService, HarvestService>();
        services.AddScoped<IFrozenStorageService, FrozenStorageService>();
        services.AddScoped<IFarmHistoryService, FarmHistoryService>();
        services.AddScoped<IBoxQrService, BoxQrService>();
        services.AddScoped<IIotService, IotService>();
        // Notification trước Alert vì AlertService phụ thuộc INotificationService
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IMediaService, MediaService>();

        services.AddScoped<IHarvestReportService, HarvestReportService>();
        services.AddScoped<IInventoryReportService, InventoryReportService>();



        services.AddHttpClient("telegram");
        services.AddHttpClient("zalo");
        services.AddHttpClient("fcm");
        services.AddHostedService<DisconnectCheckHostedService>();

        services.AddAutoMapper(typeof(DependencyInjection).Assembly);
        return services;
    }
}
