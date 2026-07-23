using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Development: migrate schema + seed 3 role user (SystemAdmin / FarmOwner / Staff).
/// Không crash app nếu Supabase transient timeout.
/// </summary>
public static class DevDbBootstrap
{
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DevDbBootstrap");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                logger.LogInformation("Migrate completed (attempt {A}).", attempt);

                await RemapLegacyRolesAsync(db, logger);
                await EnsureUserAsync(db, "sysadmin", "admin-sys@crabsense.local", "Admin hệ thống",
                    "SysAdmin@123", UserRole.SystemAdmin);
                await EnsureUserAsync(db, "admin", "admin@crabsense.local", "Admin CrabSense",
                    "Admin@123", UserRole.SystemAdmin);
                await EnsureUserAsync(db, "owner", "owner@crabsense.local", "Chủ trại",
                    "Owner@123", UserRole.FarmOwner);
                await EnsureUserAsync(db, "staff", "staff@crabsense.local", "Nhân viên",
                    "Staff@123", UserRole.Staff);

                await db.SaveChangesAsync();
                logger.LogInformation(
                    "Users sẵn sàng: sysadmin/SysAdmin@123 | owner/Owner@123 | staff/Staff@123 (admin/Admin@123 = SystemAdmin).");

                await DemoDataSeeder.SeedAsync(db, logger);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DevDbBootstrap attempt {A} failed.", attempt);
                if (attempt == 3)
                    logger.LogError("DevDbBootstrap bỏ qua sau 3 lần — API vẫn chạy; kiểm tra connection Supabase.");
                else
                    await Task.Delay(1500 * attempt);
            }
        }
    }

    /// <summary>
    /// Role lưu dạng string trong Postgres — remap legacy trước khi EF đọc entity.
    /// Admin/SystemAdmin → SystemAdmin; Operator/Sales → FarmOwner nếu chưa có owner riêng;
    /// Viewer/khác → Staff.
    /// </summary>
    private static async Task RemapLegacyRolesAsync(AppDbContext db, ILogger logger)
    {
        var updated = await db.Database.ExecuteSqlRawAsync("""
            UPDATE be."AppUsers" SET "Role" = CASE "Role"
                WHEN 'Admin' THEN 'SystemAdmin'
                WHEN 'SystemAdmin' THEN 'SystemAdmin'
                WHEN 'Operator' THEN 'Staff'
                WHEN 'Viewer' THEN 'Staff'
                WHEN 'Sales' THEN 'Staff'
                WHEN 'FarmOwner' THEN 'FarmOwner'
                WHEN 'Staff' THEN 'Staff'
                ELSE 'Staff'
            END
            WHERE "Role" NOT IN ('SystemAdmin', 'FarmOwner', 'Staff')
            """);

        if (updated > 0)
            logger.LogInformation("Remapped {N} legacy user role string(s).", updated);
    }

    private static async Task EnsureUserAsync(
        AppDbContext db, string username, string email, string fullName, string password, UserRole role)
    {
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null)
        {
            db.AppUsers.Add(new AppUser
            {
                Username = username,
                Email = email,
                FullName = fullName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = role,
                IsActive = true
            });
            return;
        }

        user.Role = role;
        user.FullName = fullName;
        user.IsActive = true;
        if (string.IsNullOrWhiteSpace(user.Email))
            user.Email = email;
    }
}
