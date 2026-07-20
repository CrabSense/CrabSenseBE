using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Repositories;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CrabSenseBE.Application.Interfaces;

namespace CrabSenseBE.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // EF Core + PostgreSQL
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                config.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
            ));

        // Repositories
        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // JWT
        services.AddScoped<IJwtService, JwtService>();

<<<<<<< HEAD
        // Storage (MinIO)
        services.AddScoped<IStorageService, MinioStorageService>();
=======
        // Storage (MinIO) — HDF5 edge
        services.AddSingleton<IStorageService, MinioStorageService>();
>>>>>>> 2ac811f2a030cae27ef3979cceb2ed5204e97e08

        // Media (ảnh / video / log) — Google Drive shared folder (hoặc Local fallback)
        var mediaProvider = (config["MediaStorage:Provider"] ?? "Local").Trim();
        if (mediaProvider.Equals("GoogleDrive", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IMediaStorageService, GoogleDriveMediaStorageService>();
        else
            services.AddSingleton<IMediaStorageService, LocalMediaStorageService>();

        return services;
    }
}
