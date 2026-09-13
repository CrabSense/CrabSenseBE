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
                try
                {
                    await EnsureSchemaReadyAsync(db, logger);
                }
                catch (Exception migrateEx)
                {
                    logger.LogWarning(
                        migrateEx,
                        "EF migrate incomplete on attempt {A}; applying idempotent schema ensures.",
                        attempt);
                }

                await EnsureBoxesMobileSchemaAsync(db, logger);
                await EnsureFarmingAreaProfileSchemaAsync(db, logger);
                await EnsureFarmingRowProfileSchemaAsync(db, logger);
                await EnsureCrabProfileSchemaAsync(db, logger);
                await EnsureCrabLotInboundSchemaAsync(db, logger);
                await EnsureRasFlowSchemaAsync(db, logger);
                await EnsureDeviceControllerSchemaAsync(db, logger);
                await EnsureWaterAnalysisSchemaAsync(db, logger);
                await EnsureFarmOperationLogColumnsAsync(db, logger);
                await EnsureHarvestSalesWorkflowSchemaAsync(db, logger);
                await EnsureOrphanAlertCleanupAsync(db, logger);
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

                // DemoDataSeeder đã tắt — không auto-insert khu/dãy/hộp/cua khi start API.
                await EnsureBoxQrsForAllBoxesAsync(db, logger);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DevDbBootstrap attempt {A} failed.", attempt);
                db.ChangeTracker.Clear();
                if (attempt == 3)
                    logger.LogError("DevDbBootstrap bỏ qua sau 3 lần — API vẫn chạy; kiểm tra connection Supabase.");
                else
                    await Task.Delay(1500 * attempt);
            }
        }
    }

    /// <summary>
    /// Apply EF migrations. If a pending migration tries to add a table/column/index
    /// that already exists (42P07 / 42701 / 42710), record only that migration and
    /// continue — do not baseline every remaining migration (that would skip Crab).
    /// </summary>
    private static async Task EnsureSchemaReadyAsync(AppDbContext db, ILogger logger)
    {
        for (var i = 0; i < 24; i++)
        {
            try
            {
                await db.Database.MigrateAsync();
                return;
            }
            catch (Exception ex) when (IsAlreadyExistsSchemaError(ex))
            {
                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
                if (pending.Count == 0)
                {
                    logger.LogWarning(ex, "Schema object already exists and EF has no pending migrations.");
                    return;
                }

                var next = pending[0];
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO be."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                     VALUES ({next}, {"8.0.0"})
                     ON CONFLICT ("MigrationId") DO NOTHING
                     """);
                logger.LogWarning(
                    "Recorded {MigrationId} as applied because the object already exists. Continuing remaining migrations.",
                    next);
            }
        }

        throw new InvalidOperationException(
            "Could not apply EF migrations after skipping already-existing objects.");
    }

    private static bool IsAlreadyExistsSchemaError(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is PostgresException pg &&
                pg.SqlState is "42P07" or "42701" or "42710")
                return true;
        }

        return false;
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
    /// Idempotent khu profile columns + AREA-A01 backfill (works if EF history was baselined).
    /// </summary>
    private static async Task EnsureFarmingAreaProfileSchemaAsync(AppDbContext db, ILogger logger)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE be."FarmingAreas"
            ADD COLUMN IF NOT EXISTS "Code" character varying(32) NOT NULL DEFAULT '',
            ADD COLUMN IF NOT EXISTS "Location" character varying(256) NULL,
            ADD COLUMN IF NOT EXISTS "Address" text NOT NULL DEFAULT '',
            ADD COLUMN IF NOT EXISTS "Region" text NULL,
            ADD COLUMN IF NOT EXISTS "AreaSquareMeters" numeric(12,2) NULL,
            ADD COLUMN IF NOT EXISTS "EstablishedAt" timestamp with time zone NULL,
            ADD COLUMN IF NOT EXISTS "AvatarUrl" text NULL,
            ADD COLUMN IF NOT EXISTS "Status" text NOT NULL DEFAULT 'Active';

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FarmingAreas_Code"
            ON be."FarmingAreas" ("Code");
            """).ConfigureAwait(false);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE be."FarmingAreas"
                SET
                    "Status" = CASE
                        WHEN "Status" IS NULL OR btrim("Status") = '' THEN
                            CASE WHEN "IsActive" THEN 'Active' ELSE 'Closed' END
                        ELSE "Status"
                    END;

                WITH max_existing AS (
                    SELECT COALESCE(MAX(
                        CASE
                            WHEN "Code" ~ '^AREA-[A-Za-z][0-9]{2}$' THEN
                                (ASCII(UPPER(SUBSTRING("Code" FROM 6 FOR 1))) - 65) * 99
                                + CAST(SUBSTRING("Code" FROM 7 FOR 2) AS int)
                            ELSE 0
                        END
                    ), 0) AS n
                    FROM be."FarmingAreas"
                ),
                numbered AS (
                    SELECT a."Id", ROW_NUMBER() OVER (ORDER BY a."CreatedAt", a."Id") AS seq
                    FROM be."FarmingAreas" a
                    WHERE a."Code" IS NULL
                       OR btrim(a."Code") = ''
                       OR a."Code" ~* '^FARM-'
                       OR a."Code" ~ '^AREA-[0-9]+$'
                )
                UPDATE be."FarmingAreas" a
                SET "Code" = 'AREA-'
                    || CHR((65 + (((max_existing.n + numbered.seq) - 1) / 99))::int)
                    || LPAD(((((max_existing.n + numbered.seq) - 1) % 99) + 1)::text, 2, '0')
                FROM numbered, max_existing
                WHERE a."Id" = numbered."Id";
                """).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FarmingArea AREA-xxx backfill skipped (column Location is already ensured).");
        }

        logger.LogInformation("Ensured FarmingArea khu columns and AREA-xxx codes.");
    }

    /// <summary>Idempotent dãy columns + DAY-A01 backfill.</summary>
    private static async Task EnsureFarmingRowProfileSchemaAsync(AppDbContext db, ILogger logger)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE be."FarmingRows"
            ADD COLUMN IF NOT EXISTS "Code" character varying(32) NOT NULL DEFAULT '',
            ADD COLUMN IF NOT EXISTS "Location" character varying(256) NULL,
            ADD COLUMN IF NOT EXISTS "Description" text NULL,
            ADD COLUMN IF NOT EXISTS "Status" text NOT NULL DEFAULT 'Active',
            ADD COLUMN IF NOT EXISTS "SortOrder" integer NOT NULL DEFAULT 0;
            """).ConfigureAwait(false);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE be."FarmingRows"
                SET "Status" = CASE
                    WHEN "Status" IS NULL OR btrim("Status") = '' THEN
                        CASE WHEN "IsActive" THEN 'Active' ELSE 'Closed' END
                    ELSE "Status"
                END;

                WITH max_existing AS (
                    SELECT COALESCE(MAX(
                        CASE
                            WHEN "Code" ~ '^DAY-[A-Za-z][0-9]{2}$' THEN
                                (ASCII(UPPER(SUBSTRING("Code" FROM 6 FOR 1))) - 65) * 99
                                + CAST(SUBSTRING("Code" FROM 7 FOR 2) AS int)
                            ELSE 0
                        END
                    ), 0) AS n
                    FROM be."FarmingRows"
                ),
                numbered AS (
                    SELECT r."Id", ROW_NUMBER() OVER (ORDER BY r."CreatedAt", r."Id") AS seq
                    FROM be."FarmingRows" r
                    WHERE r."Code" IS NULL
                       OR btrim(r."Code") = ''
                       OR r."Code" ~ '^DAY-[0-9]+$'
                )
                UPDATE be."FarmingRows" r
                SET "Code" = 'DAY-'
                    || CHR((65 + (((max_existing.n + numbered.seq) - 1) / 99))::int)
                    || LPAD(((((max_existing.n + numbered.seq) - 1) % 99) + 1)::text, 2, '0')
                FROM numbered, max_existing
                WHERE r."Id" = numbered."Id";

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_FarmingRows_Code"
                ON be."FarmingRows" ("Code");
                """).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FarmingRow DAY-xxx backfill skipped (columns already ensured).");
        }

        logger.LogInformation("Ensured FarmingRow dãy columns and DAY-xxx codes.");
    }

    /// <summary>Idempotent cua profile columns + history tables. ADD COLUMN tách khỏi backfill.</summary>
    private static async Task EnsureCrabProfileSchemaAsync(AppDbContext db, ILogger logger)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE be."Crabs"
            ADD COLUMN IF NOT EXISTS "Code" character varying(32) NOT NULL DEFAULT '',
            ADD COLUMN IF NOT EXISTS "QrCode" character varying(48) NULL,
            ADD COLUMN IF NOT EXISTS "CrabType" text NULL,
            ADD COLUMN IF NOT EXISTS "Gender" text NOT NULL DEFAULT 'Unknown',
            ADD COLUMN IF NOT EXISTS "InitialWeightGram" numeric(10,2) NULL,
            ADD COLUMN IF NOT EXISTS "CarapaceWidthMm" numeric(8,2) NULL,
            ADD COLUMN IF NOT EXISTS "CarapaceLengthMm" numeric(8,2) NULL,
            ADD COLUMN IF NOT EXISTS "InitialCondition" text NULL,
            ADD COLUMN IF NOT EXISTS "Notes" text NULL,
            ADD COLUMN IF NOT EXISTS "Condition" text NOT NULL DEFAULT 'Normal',
            ADD COLUMN IF NOT EXISTS "AiPrediction" text NULL,
            ADD COLUMN IF NOT EXISTS "AiConfidence" numeric(5,2) NULL;

            ALTER TABLE be."QrCodes"
            ADD COLUMN IF NOT EXISTS "CrabId" uuid NULL;

            CREATE TABLE IF NOT EXISTS be."CrabStatusHistories" (
                "Id" uuid NOT NULL,
                "CrabId" uuid NOT NULL,
                "OldCondition" text NULL,
                "NewCondition" text NOT NULL,
                "OldStatus" text NULL,
                "NewStatus" text NOT NULL,
                "ChangedAt" timestamp with time zone NOT NULL,
                "Source" text NOT NULL DEFAULT 'system',
                "Reason" text NULL,
                "ChangedByUserId" uuid NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_CrabStatusHistories" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS be."CrabWeightHistories" (
                "Id" uuid NOT NULL,
                "CrabId" uuid NOT NULL,
                "WeightGram" numeric(10,2) NOT NULL,
                "MeasuredAt" timestamp with time zone NOT NULL,
                "Source" text NOT NULL DEFAULT 'manual',
                "Notes" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_CrabWeightHistories" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS be."CrabAiAnalyses" (
                "Id" uuid NOT NULL,
                "CrabId" uuid NOT NULL,
                "BoxId" uuid NULL,
                "Prediction" text NOT NULL,
                "Confidence" numeric(5,2) NOT NULL,
                "ActivityLevel" text NULL,
                "AnomalyNote" text NULL,
                "MediaUrl" text NULL,
                "ModelVersion" text NULL,
                "AnalyzedAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_CrabAiAnalyses" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS be."CrabHarvestHistories" (
                "Id" uuid NOT NULL,
                "CrabId" uuid NOT NULL,
                "HarvestLineId" uuid NULL,
                "HarvestedAt" timestamp with time zone NOT NULL,
                "WeightGram" numeric(10,2) NULL,
                "Grade" text NULL,
                "Notes" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_CrabHarvestHistories" PRIMARY KEY ("Id")
            );

            CREATE INDEX IF NOT EXISTS "IX_CrabStatusHistories_CrabId" ON be."CrabStatusHistories" ("CrabId");
            CREATE INDEX IF NOT EXISTS "IX_CrabWeightHistories_CrabId" ON be."CrabWeightHistories" ("CrabId");
            CREATE INDEX IF NOT EXISTS "IX_CrabAiAnalyses_CrabId" ON be."CrabAiAnalyses" ("CrabId");
            CREATE INDEX IF NOT EXISTS "IX_CrabHarvestHistories_CrabId" ON be."CrabHarvestHistories" ("CrabId");
            CREATE INDEX IF NOT EXISTS "IX_QrCodes_CrabId" ON be."QrCodes" ("CrabId");
            """).ConfigureAwait(false);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE be."Crabs"
                SET
                    "InitialWeightGram" = COALESCE("InitialWeightGram", "WeightGram"),
                    "InitialCondition" = COALESCE(NULLIF(btrim("InitialCondition"), ''), 'Khỏe mạnh'),
                    "Gender" = CASE WHEN "Gender" IS NULL OR btrim("Gender") = '' THEN 'Unknown' ELSE "Gender" END,
                    "Condition" = CASE
                        WHEN "Condition" IS NULL OR btrim("Condition") = '' OR "Condition" = 'Normal' THEN
                            CASE
                                WHEN "Status" IN (2) THEN 'Dead'
                                WHEN "Status" IN (4) THEN 'Harvested'
                                WHEN "Status" IN (3, 5) THEN 'Problem'
                                WHEN "Status" IN (1) THEN 'Molting'
                                WHEN lower(replace(replace(COALESCE("MoltingStage",''), '-', ''), '_', '')) IN ('premolt', 'pre') THEN 'Premolt'
                                WHEN lower(replace(replace(COALESCE("MoltingStage",''), '-', ''), '_', '')) IN ('molting', 'molt') THEN 'Molting'
                                WHEN lower(replace(replace(COALESCE("MoltingStage",''), '-', ''), '_', '')) IN ('softshell', 'soft', 'postmolt', 'post') THEN 'Softshell'
                                ELSE "Condition"
                            END
                        ELSE "Condition"
                    END;

                WITH numbered AS (
                    SELECT c."Id", ROW_NUMBER() OVER (ORDER BY c."CreatedAt", c."Id") AS seq
                    FROM be."Crabs" c
                    WHERE c."Code" IS NULL OR btrim(c."Code") = ''
                )
                UPDATE be."Crabs" c
                SET "Code" = 'CRAB-' || LPAD(numbered.seq::text, 4, '0')
                FROM numbered
                WHERE c."Id" = numbered."Id";

                UPDATE be."Crabs"
                SET
                    "Tag" = COALESCE(NULLIF(btrim("Tag"), ''), "Code"),
                    "QrCode" = COALESCE(NULLIF(btrim("QrCode"), ''), 'QR-' || "Code")
                WHERE btrim("Code") <> '';
                """).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Crab code/condition backfill skipped (columns already ensured).");
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Crabs_Code"
                ON be."Crabs" ("Code");
                """).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Crab Code unique index skipped.");
        }

        logger.LogInformation("Ensured Crab profile columns and history tables.");
    }

    private static async Task EnsureCrabLotInboundSchemaAsync(AppDbContext db, ILogger logger)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE be."CrabLots"
            ADD COLUMN IF NOT EXISTS "Name" character varying(128) NOT NULL DEFAULT '',
            ADD COLUMN IF NOT EXISTS "TotalWeightKg" numeric(12,3) NULL,
            ADD COLUMN IF NOT EXISTS "WeightMinGram" numeric(10,2) NULL,
            ADD COLUMN IF NOT EXISTS "WeightMaxGram" numeric(10,2) NULL,
            ADD COLUMN IF NOT EXISTS "UnitPriceVndPerKg" numeric(14,2) NULL,
            ADD COLUMN IF NOT EXISTS "CrabCostVnd" numeric(14,2) NULL,
            ADD COLUMN IF NOT EXISTS "ShippingCostVnd" numeric(14,2) NULL,
            ADD COLUMN IF NOT EXISTS "OtherCostVnd" numeric(14,2) NULL,
            ADD COLUMN IF NOT EXISTS "TotalCostVnd" numeric(14,2) NULL,
            ADD COLUMN IF NOT EXISTS "Condition" character varying(16) NOT NULL DEFAULT 'Good',
            ADD COLUMN IF NOT EXISTS "DeadOnArrival" integer NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "Status" character varying(16) NOT NULL DEFAULT 'Pending';
            """).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE be."CrabLots"
            SET "Name" = "LotCode"
            WHERE btrim("Name") = '';
            """).ConfigureAwait(false);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CrabLots_LotCode"
                ON be."CrabLots" ("LotCode");
                """).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CrabLot LotCode unique index skipped.");
        }

        logger.LogInformation("Ensured CrabLot inbound columns.");
    }

    private static async Task EnsureRasFlowSchemaAsync(AppDbContext db, ILogger logger)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE be."WaterSystems"
            ADD COLUMN IF NOT EXISTS "Status" character varying(32) NOT NULL DEFAULT 'active',
            ADD COLUMN IF NOT EXISTS "FlowStatus" character varying(32) NULL,
            ADD COLUMN IF NOT EXISTS "Description" text NULL;

            ALTER TABLE be."Sensors"
            ADD COLUMN IF NOT EXISTS "RasComponentId" uuid NULL;
            """).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS be."RasComponents" (
                "Id" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "WaterSystemId" uuid NOT NULL,
                "Code" character varying(64) NOT NULL,
                "Name" character varying(128) NOT NULL,
                "Type" character varying(32) NOT NULL,
                "Status" character varying(32) NOT NULL,
                "Position" integer NOT NULL,
                "Capacity" numeric(12,2) NULL,
                "Description" text NULL,
                "NodeType" text NULL,
                "IconKey" text NULL,
                "RelayDeviceId" uuid NULL,
                "RelayChannel" text NULL,
                "ParamDefaultsJson" text NULL,
                "HasRelay" boolean NOT NULL,
                "IsOn" boolean NOT NULL,
                "ControlMode" text NULL,
                "LastCommandAt" timestamp with time zone NULL,
                "RunStartedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_RasComponents" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_RasComponents_WaterSystems_WaterSystemId"
                    FOREIGN KEY ("WaterSystemId") REFERENCES be."WaterSystems" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_RasComponents_Devices_RelayDeviceId"
                    FOREIGN KEY ("RelayDeviceId") REFERENCES be."Devices" ("Id") ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS "IX_RasComponents_WaterSystemId_Position"
                ON be."RasComponents" ("WaterSystemId", "Position");
            CREATE INDEX IF NOT EXISTS "IX_RasComponents_RelayDeviceId"
                ON be."RasComponents" ("RelayDeviceId");

            CREATE TABLE IF NOT EXISTS be."WaterFlows" (
                "Id" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "WaterSystemId" uuid NOT NULL,
                "FromComponentId" uuid NOT NULL,
                "ToComponentId" uuid NOT NULL,
                "FlowRate" numeric(12,2) NULL,
                "Status" character varying(32) NOT NULL,
                "SortOrder" integer NOT NULL,
                CONSTRAINT "PK_WaterFlows" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_WaterFlows_WaterSystems_WaterSystemId"
                    FOREIGN KEY ("WaterSystemId") REFERENCES be."WaterSystems" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_WaterFlows_RasComponents_FromComponentId"
                    FOREIGN KEY ("FromComponentId") REFERENCES be."RasComponents" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_WaterFlows_RasComponents_ToComponentId"
                    FOREIGN KEY ("ToComponentId") REFERENCES be."RasComponents" ("Id") ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS "IX_WaterFlows_WaterSystemId" ON be."WaterFlows" ("WaterSystemId");
            CREATE INDEX IF NOT EXISTS "IX_WaterFlows_FromComponentId" ON be."WaterFlows" ("FromComponentId");
            CREATE INDEX IF NOT EXISTS "IX_WaterFlows_ToComponentId" ON be."WaterFlows" ("ToComponentId");

            ALTER TABLE be."RasComponents"
            ADD COLUMN IF NOT EXISTS "LastCommandAt" timestamp with time zone NULL,
            ADD COLUMN IF NOT EXISTS "RunStartedAt" timestamp with time zone NULL;
            """).ConfigureAwait(false);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Sensors_RasComponentId" ON be."Sensors" ("RasComponentId");
                ALTER TABLE be."Sensors"
                DROP CONSTRAINT IF EXISTS "FK_Sensors_RasComponents_RasComponentId";
                ALTER TABLE be."Sensors"
                ADD CONSTRAINT "FK_Sensors_RasComponents_RasComponentId"
                    FOREIGN KEY ("RasComponentId") REFERENCES be."RasComponents" ("Id") ON DELETE SET NULL;
                """).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sensors.RasComponentId FK skipped.");
        }

        logger.LogInformation("Ensured RAS component / water-flow schema.");
    }

    private static async Task EnsureDeviceControllerSchemaAsync(AppDbContext db, ILogger logger)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE be."Devices"
            ADD COLUMN IF NOT EXISTS "Name" text NULL,
            ADD COLUMN IF NOT EXISTS "MacAddress" character varying(64) NULL,
            ADD COLUMN IF NOT EXISTS "IpAddress" character varying(64) NULL,
            ADD COLUMN IF NOT EXISTS "FarmingAreaId" uuid NULL;

            CREATE INDEX IF NOT EXISTS "IX_Devices_FarmingAreaId"
                ON be."Devices" ("FarmingAreaId");
            """).ConfigureAwait(false);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE be."Devices"
                DROP CONSTRAINT IF EXISTS "FK_Devices_FarmingAreas_FarmingAreaId";
                ALTER TABLE be."Devices"
                ADD CONSTRAINT "FK_Devices_FarmingAreas_FarmingAreaId"
                    FOREIGN KEY ("FarmingAreaId") REFERENCES be."FarmingAreas" ("Id") ON DELETE SET NULL;
                """).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Devices.FarmingAreaId FK skipped.");
        }

        logger.LogInformation("Ensured Device/Controller columns (Name, MAC, IP, khu).");
    }

    private static async Task EnsureWaterAnalysisSchemaAsync(AppDbContext db, ILogger logger)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS be."WaterAnalysisRuns" (
                "Id" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "FarmingAreaId" uuid NOT NULL,
                "Status" character varying(32) NOT NULL,
                "CurrentStep" integer NOT NULL,
                "StartedAt" timestamp with time zone NOT NULL,
                "LastStepAt" timestamp with time zone NOT NULL,
                "CompletedAt" timestamp with time zone NULL,
                "Ph" numeric(8,3) NULL,
                "Nh3" numeric(8,4) NULL,
                "No2" numeric(8,4) NULL,
                "No3" numeric(8,2) NULL,
                "ImageUrl" text NULL,
                "Error" text NULL,
                "Source" character varying(64) NOT NULL,
                CONSTRAINT "PK_WaterAnalysisRuns" PRIMARY KEY ("Id")
            );

            CREATE INDEX IF NOT EXISTS "IX_WaterAnalysisRuns_FarmingAreaId_StartedAt"
                ON be."WaterAnalysisRuns" ("FarmingAreaId", "StartedAt");
            """).ConfigureAwait(false);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE be."WaterAnalysisRuns"
                DROP CONSTRAINT IF EXISTS "FK_WaterAnalysisRuns_FarmingAreas_FarmingAreaId";
                ALTER TABLE be."WaterAnalysisRuns"
                ADD CONSTRAINT "FK_WaterAnalysisRuns_FarmingAreas_FarmingAreaId"
                    FOREIGN KEY ("FarmingAreaId") REFERENCES be."FarmingAreas" ("Id") ON DELETE CASCADE;
                """).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WaterAnalysisRuns.FarmingAreaId FK skipped.");
        }

        logger.LogInformation("Ensured WaterAnalysisRuns (colorimetric).");
    }

    private static async Task EnsureFarmOperationLogColumnsAsync(AppDbContext db, ILogger logger)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE be."FarmOperations"
            ADD COLUMN IF NOT EXISTS "Source" text NOT NULL DEFAULT 'manual',
            ADD COLUMN IF NOT EXISTS "LocationLabel" text NULL;
            """).ConfigureAwait(false);
        logger.LogInformation("Ensured FarmOperations.Source / LocationLabel.");
    }

    /// <summary>
    /// Additive columns for harvest → inventory → sale. Không xóa cột cũ.
    /// </summary>
    private static async Task EnsureHarvestSalesWorkflowSchemaAsync(AppDbContext db, ILogger logger)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE be."HarvestVouchers"
            ADD COLUMN IF NOT EXISTS "FarmingAreaId" uuid NULL,
            ADD COLUMN IF NOT EXISTS "PerformedByName" text NULL,
            ADD COLUMN IF NOT EXISTS "PhotoUrlsJson" text NOT NULL DEFAULT '[]';

            ALTER TABLE be."HarvestLines"
            ADD COLUMN IF NOT EXISTS "ConditionLabel" text NULL,
            ADD COLUMN IF NOT EXISTS "PhotoUrlsJson" text NOT NULL DEFAULT '[]',
            ADD COLUMN IF NOT EXISTS "CrabCode" text NULL,
            ADD COLUMN IF NOT EXISTS "AreaName" text NULL,
            ADD COLUMN IF NOT EXISTS "RowName" text NULL,
            ADD COLUMN IF NOT EXISTS "BoxCode" text NULL,
            ADD COLUMN IF NOT EXISTS "LotCode" text NULL,
            ADD COLUMN IF NOT EXISTS "Result" text NOT NULL DEFAULT 'passed';

            ALTER TABLE be."SalesOrders"
            ADD COLUMN IF NOT EXISTS "FarmingAreaId" uuid NULL,
            ADD COLUMN IF NOT EXISTS "SellerName" text NULL,
            ADD COLUMN IF NOT EXISTS "PaymentStatus" text NOT NULL DEFAULT 'Pending',
            ADD COLUMN IF NOT EXISTS "SubtotalAmount" numeric NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "DiscountAmount" numeric NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "ShippingFee" numeric NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "PaidAmount" numeric NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "PaymentMethod" text NULL,
            ADD COLUMN IF NOT EXISTS "DeliveryStatus" text NULL;

            ALTER TABLE be."SalesOrderLines"
            ADD COLUMN IF NOT EXISTS "CrabId" uuid NULL,
            ADD COLUMN IF NOT EXISTS "CrabCode" text NULL,
            ADD COLUMN IF NOT EXISTS "CrabType" text NULL,
            ADD COLUMN IF NOT EXISTS "WeightGram" numeric NULL,
            ADD COLUMN IF NOT EXISTS "Quantity" integer NOT NULL DEFAULT 1;

            CREATE INDEX IF NOT EXISTS "IX_HarvestVouchers_FarmingAreaId"
            ON be."HarvestVouchers" ("FarmingAreaId");
            CREATE INDEX IF NOT EXISTS "IX_SalesOrders_FarmingAreaId"
            ON be."SalesOrders" ("FarmingAreaId");
            CREATE INDEX IF NOT EXISTS "IX_SalesOrderLines_CrabId"
            ON be."SalesOrderLines" ("CrabId");
            """).ConfigureAwait(false);
        logger.LogInformation("Ensured harvest/sales workflow columns.");
    }

    /// <summary>
    /// Alert còn SensorId nhưng sensor đã bị xóa (seed dở) — gỡ trước khi SaveChanges.
    /// </summary>
    private static async Task EnsureOrphanAlertCleanupAsync(AppDbContext db, ILogger logger)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM be."Notifications" n
                WHERE n."AlertId" IN (
                    SELECT a."Id" FROM be."Alerts" a
                    WHERE a."SensorId" IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1 FROM be."Sensors" s WHERE s."Id" = a."SensorId")
                );

                DELETE FROM be."Alerts" a
                WHERE a."SensorId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM be."Sensors" s WHERE s."Id" = a."SensorId");
                """).ConfigureAwait(false);
            logger.LogInformation("Orphan Alerts/Notifications cleanup done.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Orphan alert cleanup skipped.");
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
