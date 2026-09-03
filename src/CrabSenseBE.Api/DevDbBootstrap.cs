using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
                await EnsureSchemaReadyAsync(db, logger);
                await EnsureBoxesMobileSchemaAsync(db, logger);
                logger.LogInformation("Schema ready (attempt {A}).", attempt);

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
                await EnsureBoxQrsForAllBoxesAsync(db, logger);
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
    /// Apply EF migrations. If Supabase already has schema but empty history
    /// (42P07 already exists), baseline history then apply remaining only.
    /// </summary>
    private static async Task EnsureSchemaReadyAsync(AppDbContext db, ILogger logger)
    {
        try
        {
            await db.Database.MigrateAsync();
            return;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P07")
        {
            logger.LogWarning("Schema objects already exist — baselining EF migration history.");
        }
        catch (Exception ex) when (ex.InnerException is PostgresException { SqlState: "42P07" })
        {
            logger.LogWarning("Schema objects already exist — baselining EF migration history.");
        }

        await BaselineMigrationHistoryAsync(db, logger);
        await db.Database.MigrateAsync();
    }

    private static async Task BaselineMigrationHistoryAsync(AppDbContext db, ILogger logger)
    {
        var all = db.Database.GetMigrations().ToList();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var missing = all.Except(applied).ToList();
        if (missing.Count == 0) return;

        foreach (var id in missing)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO be."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                 VALUES ({id}, {"8.0.0"})
                 ON CONFLICT ("MigrationId") DO NOTHING
                 """);
        }

        logger.LogInformation("Baselined {N} migration(s) into be.__EFMigrationsHistory.", missing.Count);
    }

    /// <summary>
    /// Idempotent DDL for Boxes tab / inspections / farm ops (works even if EF history was baselined).
    /// </summary>
    private static async Task EnsureBoxesMobileSchemaAsync(AppDbContext db, ILogger logger)
    {
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE be."Inspections"
            ADD COLUMN IF NOT EXISTS "BoxId" uuid NULL,
            ADD COLUMN IF NOT EXISTS "MoltingStatus" text NULL,
            ADD COLUMN IF NOT EXISTS "HealthStatus" text NULL,
            ADD COLUMN IF NOT EXISTS "WeightGram" numeric NULL,
            ADD COLUMN IF NOT EXISTS "RelatedMediaId" uuid NULL,
            ADD COLUMN IF NOT EXISTS "PhotoUrlsJson" text NULL,
            ADD COLUMN IF NOT EXISTS "OperatorName" text NULL,
            ADD COLUMN IF NOT EXISTS "AiAgreement" boolean NULL;

            ALTER TABLE be."AiDetections"
            ADD COLUMN IF NOT EXISTS "BoxId" uuid NULL,
            ADD COLUMN IF NOT EXISTS "MediaId" uuid NULL,
            ADD COLUMN IF NOT EXISTS "Status" text NOT NULL DEFAULT 'pending';

            ALTER TABLE be."Crabs"
            ADD COLUMN IF NOT EXISTS "ImageUrlsJson" text NOT NULL DEFAULT '[]';

            CREATE TABLE IF NOT EXISTS be."FarmOperations" (
                "Id" uuid NOT NULL,
                "Type" text NOT NULL,
                "BoxIdsJson" text NOT NULL,
                "Quantity" numeric NULL,
                "Unit" text NULL,
                "Notes" text NOT NULL,
                "PhotoUrlsJson" text NOT NULL,
                "Timestamp" timestamp with time zone NOT NULL,
                "OperatorId" uuid NOT NULL,
                "OperatorName" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_FarmOperations" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS be."SaleTransactions" (
                "Id" uuid NOT NULL,
                "BuyerName" text NOT NULL,
                "BuyerContact" text NULL,
                "Quantity" numeric NOT NULL,
                "UnitPrice" numeric NOT NULL,
                "TotalAmount" numeric NOT NULL,
                "PaymentMethod" text NOT NULL,
                "PaymentStatus" text NOT NULL,
                "SaleDate" timestamp with time zone NOT NULL,
                "FarmingAreaId" uuid NULL,
                "BoxId" uuid NULL,
                "OperatorId" uuid NOT NULL,
                "OperatorName" text NOT NULL,
                "Notes" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_SaleTransactions" PRIMARY KEY ("Id")
            );
            """).ConfigureAwait(false);

        logger.LogInformation("Ensured Boxes/Inspections/FarmOperations/Sales mobile schema columns.");
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
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        if (string.IsNullOrWhiteSpace(user.Email))
            user.Email = email;
    }

    /// <summary>
    /// Idempotent: create active QR row for every box missing one.
    /// Sticker value = box.Code (scan also accepts CRABSENSE:BOX:{code}).
    /// </summary>
    private static async Task EnsureBoxQrsForAllBoxesAsync(AppDbContext db, ILogger logger)
    {
        var boxes = await db.Boxes.AsNoTracking().ToListAsync();
        if (boxes.Count == 0)
        {
            logger.LogInformation("No boxes — skip QR ensure.");
            return;
        }

        var existingBoxIds = (await db.QrCodes
                .Where(q => q.IsActive && q.EntityType == "box" && q.BoxId != null)
                .Select(q => q.BoxId!.Value)
                .ToListAsync())
            .ToHashSet();

        var usedCodes = (await db.QrCodes.Select(q => q.Code).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var box in boxes)
        {
            if (existingBoxIds.Contains(box.Id)) continue;

            var code = string.IsNullOrWhiteSpace(box.Code)
                ? $"BOX-{box.Id:N}"[..12]
                : box.Code.Trim();
            if (usedCodes.Contains(code))
                code = $"{box.Code}-{box.Id.ToString("N")[..6].ToUpperInvariant()}";

            db.QrCodes.Add(new QrCode
            {
                Code = code,
                EntityType = "box",
                BoxId = box.Id,
                IsActive = true,
                Payload =
                    $"{{\"type\":\"box\",\"boxId\":\"{box.Id}\",\"boxCode\":\"{box.Code}\",\"crabsense\":\"CRABSENSE:BOX:{box.Code}\"}}",
                ScanCount = 0,
                CreatedAt = DateTime.UtcNow
            });
            usedCodes.Add(code);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Created {N} box QR code(s) for existing boxes.", added);
        }
        else
        {
            logger.LogInformation("All {N} boxes already have active QR codes.", boxes.Count);
        }
    }
}
