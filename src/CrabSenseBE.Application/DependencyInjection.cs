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
        services.AddScoped<IIotService, IotService>();

        // AutoMapper
        services.AddAutoMapper(typeof(DependencyInjection).Assembly);

        return services;
    }
}
