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
        services.AddScoped<ISurvivalRateReportService, SurvivalRateReportService>();
        services.AddScoped<IMoltingReportService, MoltingReportService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IBoxOverviewService, BoxOverviewService>();
        services.AddScoped<IBoxDetailService, BoxDetailService>();
        services.AddScoped<IFarmOperationService, FarmOperationService>();
        services.AddScoped<IManualInspectionService, ManualInspectionService>();
        services.AddScoped<ICrabConditionService, CrabConditionService>();
        services.AddScoped<IAiOpsService, AiOpsService>();
        services.AddScoped<IOperationLogService, OperationLogService>();
        services.AddScoped<ISalesService, SalesService>();
        services.AddScoped<IBoxCameraService, BoxCameraService>();

        services.AddHttpClient("telegram");
        services.AddHttpClient("zalo");
        services.AddHttpClient("fcm");
        services.AddHostedService<DisconnectCheckHostedService>();

        services.AddAutoMapper(typeof(DependencyInjection).Assembly);
        return services;
    }
}
