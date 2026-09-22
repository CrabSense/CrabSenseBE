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
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    // Schema "be" matches AppDbContext.HasDefaultSchema — required for Supabase pooler.
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "be");
                    npgsql.CommandTimeout(120);
                }
            ));

        // Repositories
        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // JWT
        services.AddScoped<IJwtService, JwtService>();

        // Storage (MinIO) — HDF5 edge
        services.AddSingleton<IStorageService, MinioStorageService>();

        // Media (ảnh / video / log) — Google Drive / S3 / Local
        services.AddSingleton<S3MediaStorageService>();
        var mediaProvider = S3Settings.MediaProvider(config);
        if (mediaProvider.Equals("S3", StringComparison.OrdinalIgnoreCase)
            || mediaProvider.Equals("AwsS3", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IMediaStorageService>(sp => sp.GetRequiredService<S3MediaStorageService>());
        else if (mediaProvider.Equals("GoogleDrive", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IMediaStorageService, GoogleDriveMediaStorageService>();
        else
            services.AddSingleton<IMediaStorageService, LocalMediaStorageService>();

        // Ảnh công khai: cùng provider với MediaStorage (Drive khi Provider=GoogleDrive).
        services.AddSingleton<IPublicImageStorage>(sp =>
        {
            if (mediaProvider.Equals("GoogleDrive", StringComparison.OrdinalIgnoreCase))
                return new MediaStoragePublicImageAdapter(sp.GetRequiredService<IMediaStorageService>());
            if (mediaProvider.Equals("S3", StringComparison.OrdinalIgnoreCase)
                || mediaProvider.Equals("AwsS3", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(S3Settings.Bucket(config)))
                return sp.GetRequiredService<S3MediaStorageService>();
            return new MediaStoragePublicImageAdapter(sp.GetRequiredService<IMediaStorageService>());
        });

        // Sao lưu / phục hồi DB bằng pg_dump / pg_restore
        services.AddSingleton<IDatabaseBackupService, DatabaseBackupService>();

        return services;
    }
}
