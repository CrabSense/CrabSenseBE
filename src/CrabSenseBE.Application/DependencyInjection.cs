using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrabSenseBE.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IFarmingService, FarmingService>();
        services.AddScoped<IFarmLotService, FarmLotService>();
        services.AddScoped<IFarmHistoryService, FarmHistoryService>();
        services.AddScoped<IBoxQrService, BoxQrService>();
        services.AddScoped<IIotService, IotService>();
<<<<<<< HEAD
        // API-310 
        services.AddScoped<IHarvestReportService, HarvestReportService>();
        // AutoMapper
        services.AddAutoMapper(typeof(DependencyInjection).Assembly);
        

        

=======
        // Notification trước Alert vì AlertService phụ thuộc INotificationService
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IMediaService, MediaService>();

        services.AddHttpClient("telegram");
        services.AddHttpClient("zalo");
        services.AddHostedService<DisconnectCheckHostedService>();

        services.AddAutoMapper(typeof(DependencyInjection).Assembly);
>>>>>>> 2ac811f2a030cae27ef3979cceb2ed5204e97e08
        return services;
    }
}
