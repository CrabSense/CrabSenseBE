using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrabSenseBE.Infrastructure.Persistence;

/// <summary>
/// Idempotent demo dataset — quan hệ đầy đủ để test UI/API.
/// Chạy sau migrate + seed users (Development only).
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoAreaMarker = "Khu Demo RAS-A";

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Crabs.AnyAsync(c => c.Tag == "CRAB-A01", ct))
        {
            logger.LogInformation("Demo dataset already present — skip seed.");
            return;
        }

        // Gỡ demo dở dang (có khu nhưng chưa có cua)
        await ClearPartialDemoAsync(db, logger, ct);

        var owner = await db.AppUsers.FirstOrDefaultAsync(u => u.Username == "owner", ct)
            ?? await db.AppUsers.FirstOrDefaultAsync(u => u.Role == UserRole.FarmOwner, ct);
        if (owner is null)
        {
            logger.LogWarning("Demo seed skipped — no FarmOwner user.");
            return;
        }

        var staff = await db.AppUsers.FirstOrDefaultAsync(u => u.Username == "staff", ct);
        var now = DateTime.UtcNow;

        // ── Lots ─────────────────────────────────────────────────────────────
        var lot1 = new CrabLot
        {
            LotCode = "LOT-2026-001",
            ImportDate = now.AddDays(-45),
            Quantity = 0,
            AverageWeightGram = 180,
            SupplierName = "Nhà cung cấp Cà Mau",
            Notes = "Lô giống demo A"
        };
        var lot2 = new CrabLot
        {
            LotCode = "LOT-2026-002",
            ImportDate = now.AddDays(-20),
            Quantity = 0,
            AverageWeightGram = 195,
            SupplierName = "Nhà cung cấp Bạc Liêu",
            Notes = "Lô giống demo B"
        };
        db.CrabLots.AddRange(lot1, lot2);

        // ── Farm hierarchy ───────────────────────────────────────────────────
        var areaA = new FarmingArea
        {
            OwnerId = owner.Id,
            Name = DemoAreaMarker,
            Description = "Khu nuôi demo — có IoT + cua + thu hoạch",
            IsActive = true
        };
        var areaB = new FarmingArea
        {
            OwnerId = owner.Id,
            Name = "Khu Demo RAS-B",
            Description = "Khu phụ — ít hộp hơn",
            IsActive = true
        };
        db.FarmingAreas.AddRange(areaA, areaB);
        await db.SaveChangesAsync(ct);

        var rowA1 = new FarmingRow { FarmingAreaId = areaA.Id, Name = "Dãy A1", Capacity = 5, IsActive = true };
        var rowA2 = new FarmingRow { FarmingAreaId = areaA.Id, Name = "Dãy A2", Capacity = 3, IsActive = true };
        var rowB1 = new FarmingRow { FarmingAreaId = areaB.Id, Name = "Dãy B1", Capacity = 2, IsActive = true };
        db.FarmingRows.AddRange(rowA1, rowA2, rowB1);
        await db.SaveChangesAsync(ct);

        var boxes = new List<Box>();
        void AddBoxes(FarmingRow row, string prefix, int start, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var n = start + i;
                boxes.Add(new Box
                {
                    FarmingRowId = row.Id,
                    Code = $"{prefix}{n:D2}",
                    Status = BoxStatuses.Empty,
                    IsOccupied = false
                });
            }
        }
        AddBoxes(rowA1, "BOX-A", 1, 5);
        AddBoxes(rowA2, "BOX-A", 6, 3);
        AddBoxes(rowB1, "BOX-B", 1, 2);
        db.Boxes.AddRange(boxes);
        await db.SaveChangesAsync(ct);

        var boxA01 = boxes.First(b => b.Code == "BOX-A01");
        var boxA02 = boxes.First(b => b.Code == "BOX-A02");
        var boxA03 = boxes.First(b => b.Code == "BOX-A03");
        var boxA05 = boxes.First(b => b.Code == "BOX-A05");
        var boxA06 = boxes.First(b => b.Code == "BOX-A06");
        var boxB01 = boxes.First(b => b.Code == "BOX-B01");

        boxA01.Status = BoxStatuses.Active; boxA01.IsOccupied = true;
        boxA02.Status = BoxStatuses.Molting; boxA02.IsOccupied = true;
        boxA03.Status = BoxStatuses.Active; boxA03.IsOccupied = true;
        boxA05.Status = BoxStatuses.Quarantine; boxA05.IsOccupied = true;
        boxA06.Status = BoxStatuses.Active; boxA06.IsOccupied = true;
        boxB01.Status = BoxStatuses.Active; boxB01.IsOccupied = true;

        // ── Crabs ──────────────────────────────────────────────────────────
        var crabs = new[]
        {
            new Crab { CrabLotId = lot1.Id, Tag = "CRAB-A01", WeightGram = 210, StockedAt = now.AddDays(-30), MoltingStage = "hard-shell", Status = CrabStatus.Alive },
            new Crab { CrabLotId = lot1.Id, Tag = "CRAB-A02", WeightGram = 195, StockedAt = now.AddDays(-25), MoltingStage = "molting", Status = CrabStatus.Molting, MoltedAt = now.AddHours(-6) },
            new Crab { CrabLotId = lot1.Id, Tag = "CRAB-A03", WeightGram = 225, StockedAt = now.AddDays(-20), MoltingStage = "pre-molt", Status = CrabStatus.Alive },
            new Crab { CrabLotId = lot2.Id, Tag = "CRAB-A04", WeightGram = 188, StockedAt = now.AddDays(-15), MoltingStage = "post-molt", Status = CrabStatus.Alive, MoltedAt = now.AddDays(-2) },
            new Crab { CrabLotId = lot2.Id, Tag = "CRAB-A05", WeightGram = 240, StockedAt = now.AddDays(-10), MoltingStage = "hard-shell", Status = CrabStatus.Alive },
            new Crab { CrabLotId = lot2.Id, Tag = "CRAB-B01", WeightGram = 200, StockedAt = now.AddDays(-8), MoltingStage = "hard-shell", Status = CrabStatus.Alive },
        };
        db.Crabs.AddRange(crabs);
        lot1.Quantity = 3;
        lot2.Quantity = 3;
        await db.SaveChangesAsync(ct);

        var crabA01 = crabs[0];
        var crabA02 = crabs[1];
        var crabA03 = crabs[2];
        var crabA04 = crabs[3];
        var crabA05 = crabs[4];

        // ── Box allocations (Crab ↔ Box history) ────────────────────────────
        db.CrabBoxAllocations.AddRange(
            new CrabBoxAllocation { CrabId = crabA01.Id, BoxId = boxA01.Id, StartTime = now.AddDays(-30), Notes = "Thả nuôi ban đầu" },
            new CrabBoxAllocation { CrabId = crabA02.Id, BoxId = boxA02.Id, StartTime = now.AddDays(-25), Notes = "Chuyển từ BOX-A-01" },
            new CrabBoxAllocation { CrabId = crabA02.Id, BoxId = boxA01.Id, StartTime = now.AddDays(-35), EndTime = now.AddDays(-25), Notes = "Hộp cũ" },
            new CrabBoxAllocation { CrabId = crabA03.Id, BoxId = boxA03.Id, StartTime = now.AddDays(-20), Notes = "Thả nuôi" },
            new CrabBoxAllocation { CrabId = crabA04.Id, BoxId = boxA05.Id, StartTime = now.AddDays(-15), Notes = "Cách ly theo dõi" },
            new CrabBoxAllocation { CrabId = crabA05.Id, BoxId = boxA06.Id, StartTime = now.AddDays(-10), Notes = "Thả nuôi" },
            new CrabBoxAllocation { CrabId = crabA01.Id, BoxId = boxA01.Id, StartTime = now.AddDays(-35), EndTime = now.AddDays(-30), Notes = "Hộp cũ" },
            new CrabBoxAllocation { CrabId = crabs[5].Id, BoxId = boxB01.Id, StartTime = now.AddDays(-8), Notes = "Thả nuôi khu B" });

        db.MoltingRecords.AddRange(
            new MoltingRecord { CrabId = crabA02.Id, BoxId = boxA02.Id, MoltTime = now.AddHours(-6), WeightAfterGram = 195, Result = "success", Source = "manual", Notes = "Lột thành công" },
            new MoltingRecord { CrabId = crabA04.Id, BoxId = boxA05.Id, MoltTime = now.AddDays(-2), WeightAfterGram = 188, Result = "success", Source = "ai", Notes = "AI phát hiện" },
            new MoltingRecord { CrabId = crabA01.Id, BoxId = boxA01.Id, MoltTime = now.AddDays(-10), WeightAfterGram = 205, Result = "incomplete", Source = "manual" });

        db.BoxStatusHistories.AddRange(
            new BoxStatusHistory { BoxId = boxA02.Id, OldStatus = BoxStatuses.Active, NewStatus = BoxStatuses.Molting, OldIsOccupied = true, NewIsOccupied = true, ChangedAt = now.AddHours(-6), Reason = "Bắt đầu lột", ChangedByUserId = owner.Id },
            new BoxStatusHistory { BoxId = boxA05.Id, OldStatus = BoxStatuses.Active, NewStatus = BoxStatuses.Quarantine, OldIsOccupied = true, NewIsOccupied = true, ChangedAt = now.AddDays(-1), Reason = "Theo dõi sau lột", ChangedByUserId = staff?.Id ?? owner.Id });

        // ── IoT ────────────────────────────────────────────────────────────
        var ws = new WaterSystem { FarmingAreaId = areaA.Id, Name = "RAS-A Main", Type = "RAS", IsActive = true };
        db.WaterSystems.Add(ws);

        var devA = new Device { DeviceCode = "ESP32-DEMO-A", DeviceType = "esp32", FirmwareVersion = "1.2.0", Status = DeviceStatus.Online, BatteryLevel = 88, RssiDbm = -52, LastSeenAt = now };
        var devB = new Device { DeviceCode = "ESP32-DEMO-B", DeviceType = "esp32", FirmwareVersion = "1.2.0", Status = DeviceStatus.Online, BatteryLevel = 74, RssiDbm = -61, LastSeenAt = now };
        var cam = new Device { DeviceCode = "CAM-DEMO-01", DeviceType = "camera", FirmwareVersion = "2.0.1", Status = DeviceStatus.Online, LastSeenAt = now.AddMinutes(-3) };
        db.Devices.AddRange(devA, devB, cam);
        await db.SaveChangesAsync(ct);

        var sensors = new[]
        {
            new Sensor { SensorCode = "TEMP-DEMO-01", SensorType = "Temperature", Unit = "C", DeviceId = devA.Id, WaterSystemId = ws.Id, MinThreshold = 24, MaxThreshold = 30, IsActive = true, LastSeenAt = now },
            new Sensor { SensorCode = "PH-DEMO-01", SensorType = "pH", Unit = "pH", DeviceId = devA.Id, WaterSystemId = ws.Id, MinThreshold = 7.5m, MaxThreshold = 8.5m, IsActive = true, LastSeenAt = now },
            new Sensor { SensorCode = "DO-DEMO-01", SensorType = "DO", Unit = "mg/L", DeviceId = devA.Id, WaterSystemId = ws.Id, MinThreshold = 5, MaxThreshold = 9, IsActive = true, LastSeenAt = now },
            new Sensor { SensorCode = "SAL-DEMO-01", SensorType = "Salinity", Unit = "ppt", DeviceId = devB.Id, WaterSystemId = ws.Id, MinThreshold = 15, MaxThreshold = 35, IsActive = true, LastSeenAt = now },
        };
        db.Sensors.AddRange(sensors);
        await db.SaveChangesAsync(ct);

        var measurements = new List<WaterMeasurement>();
        foreach (var sensor in sensors)
        {
            for (var h = 23; h >= 0; h--)
            {
                var at = now.AddHours(-h);
                var value = sensor.SensorType switch
                {
                    "Temperature" => 27.5m + (h % 5) * 0.3m,
                    "pH" => 8.0m + (h % 3) * 0.1m,
                    "DO" => h < 3 ? 4.1m : 6.5m + (h % 4) * 0.2m,
                    "Salinity" => 28m + (h % 6),
                    _ => 1m
                };
                measurements.Add(new WaterMeasurement
                {
                    SensorId = sensor.Id,
                    WaterSystemId = ws.Id,
                    Value = value,
                    Unit = sensor.Unit,
                    MeasuredAt = at,
                    Source = "demo-seed"
                });
            }
        }
        db.WaterMeasurements.AddRange(measurements);

        // ── Alerts ───────────────────────────────────────────────────────────
        var thrDo = new AlertThreshold { SensorType = "DO", MinValue = 4.5m, MaxValue = 10, Severity = AlertSeverity.Warning, IsActive = true };
        var thrPh = new AlertThreshold { SensorType = "pH", MinValue = 7.0m, MaxValue = 8.8m, Severity = AlertSeverity.Critical, IsActive = true };
        var thrTemp = new AlertThreshold { SensorType = "Temperature", MinValue = 22, MaxValue = 32, Severity = AlertSeverity.Warning, IsActive = true };
        db.AlertThresholds.AddRange(thrDo, thrPh, thrTemp);

        var doSensor = sensors.First(s => s.SensorType == "DO");
        var alertActive = new Alert
        {
            SensorId = doSensor.Id,
            AlertThresholdId = thrDo.Id,
            Message = $"DO thấp: 4.1 < 4.5 (sensor {doSensor.SensorCode})",
            Severity = AlertSeverity.Warning,
            Status = AlertStatus.Active,
            TriggerValue = 4.1m
        };
        var alertResolved = new Alert
        {
            SensorId = sensors.First(s => s.SensorType == "Temperature").Id,
            Message = "Nhiệt độ cao: 31.2 > 30 (sensor TEMP-DEMO-01)",
            Severity = AlertSeverity.Warning,
            Status = AlertStatus.Resolved,
            TriggerValue = 31.2m,
            AcknowledgedAt = now.AddDays(-1)
        };
        db.Alerts.AddRange(alertActive, alertResolved);
        await db.SaveChangesAsync(ct);

        db.Notifications.Add(new Notification
        {
            UserId = owner.Id,
            AlertId = alertActive.Id,
            Title = "CrabSense Alert",
            Body = alertActive.Message,
            Channel = "in-app",
            IsRead = false
        });

        // ── Harvest & frozen ─────────────────────────────────────────────────
        var harvest = new HarvestVoucher
        {
            VoucherCode = "HV-2026-DEMO-001",
            HarvestDate = now.AddDays(-3),
            Status = HarvestStatus.Completed,
            TotalQuantity = 2,
            TotalWeightKg = 0.53m,
            Notes = "Thu hoạch demo",
            CreatedBy = owner.Id
        };
        db.HarvestVouchers.Add(harvest);
        await db.SaveChangesAsync(ct);

        db.HarvestLines.AddRange(
            new HarvestLine { HarvestVoucherId = harvest.Id, CrabId = crabA01.Id, BoxId = boxA01.Id, WeightGram = 250, Grade = "M", IsSoftshell = true, Notes = "Size M" },
            new HarvestLine { HarvestVoucherId = harvest.Id, CrabId = crabA04.Id, BoxId = boxA05.Id, WeightGram = 280, Grade = "L", IsSoftshell = false });

        db.FrozenLots.AddRange(
            new FrozenLot
            {
                LotCode = "FZ-2026-001",
                HarvestVoucherId = harvest.Id,
                FrozenDate = now.AddDays(-2),
                ExpiryDate = now.AddMonths(6),
                WeightKg = 12.5m,
                Grade = "M",
                Quantity = 50,
                Status = FrozenLotStatus.Available,
                StorageLocation = "Kho A - Ngăn 1"
            },
            new FrozenLot
            {
                LotCode = "FZ-2026-002",
                HarvestVoucherId = harvest.Id,
                FrozenDate = now.AddDays(-1),
                ExpiryDate = now.AddDays(14),
                WeightKg = 8.2m,
                Grade = "L",
                Quantity = 30,
                Status = FrozenLotStatus.Available,
                StorageLocation = "Kho A - Ngăn 2"
            });

        // ── QR ─────────────────────────────────────────────────────────────
        db.QrCodes.Add(new QrCode
        {
            Code = "QR-BOX-A01",
            EntityType = "box",
            BoxId = boxA01.Id,
            Payload = $"box:{boxA01.Id}",
            IsActive = true,
            ScanCount = 3
        });

        db.OperationLogs.Add(new OperationLog
        {
            UserId = owner.Id,
            Action = "demo_seed",
            EntityType = "FarmingArea",
            EntityId = areaA.Id,
            Details = "Demo dataset seeded",
            IpAddress = "127.0.0.1"
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Demo dataset seeded: 2 areas, {BoxCount} boxes, {CrabCount} crabs, IoT, harvest, frozen.",
            boxes.Count, crabs.Length);
    }

    private static async Task ClearPartialDemoAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var demoAreas = await db.FarmingAreas
            .Where(a => a.Name.StartsWith("Khu Demo"))
            .ToListAsync(ct);
        if (demoAreas.Count == 0) return;

        var areaIds = demoAreas.Select(a => a.Id).ToHashSet();
        var rows = await db.FarmingRows.Where(r => areaIds.Contains(r.FarmingAreaId)).ToListAsync(ct);
        var rowIds = rows.Select(r => r.Id).ToHashSet();
        var boxes = await db.Boxes.Where(b => rowIds.Contains(b.FarmingRowId)).ToListAsync(ct);
        var boxIds = boxes.Select(b => b.Id).ToHashSet();

        var crabAllocations = await db.CrabBoxAllocations.Where(a => boxIds.Contains(a.BoxId)).ToListAsync(ct);
        var crabIds = crabAllocations.Select(a => a.CrabId).ToHashSet();

        if (crabIds.Count > 0)
        {
            db.MoltingRecords.RemoveRange(await db.MoltingRecords.Where(m => crabIds.Contains(m.CrabId)).ToListAsync(ct));
            db.CrabBoxAllocations.RemoveRange(crabAllocations);
            db.HarvestLines.RemoveRange(await db.HarvestLines.Where(h => h.CrabId != null && crabIds.Contains(h.CrabId.Value)).ToListAsync(ct));
            db.Crabs.RemoveRange(await db.Crabs.Where(c => crabIds.Contains(c.Id)).ToListAsync(ct));
        }

        db.BoxStatusHistories.RemoveRange(await db.BoxStatusHistories.Where(h => boxIds.Contains(h.BoxId)).ToListAsync(ct));
        db.QrCodes.RemoveRange(await db.QrCodes.Where(q => q.BoxId != null && boxIds.Contains(q.BoxId.Value)).ToListAsync(ct));
        db.Boxes.RemoveRange(boxes);
        db.FarmingRows.RemoveRange(rows);

        var wsIds = await db.WaterSystems.Where(w => w.FarmingAreaId != null && areaIds.Contains(w.FarmingAreaId.Value))
            .Select(w => w.Id).ToListAsync(ct);
        if (wsIds.Count > 0)
        {
            var sensorIds = await db.Sensors.Where(s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value))
                .Select(s => s.Id).ToListAsync(ct);
            if (sensorIds.Count > 0)
                db.WaterMeasurements.RemoveRange(await db.WaterMeasurements.Where(m => sensorIds.Contains(m.SensorId)).ToListAsync(ct));
            db.Sensors.RemoveRange(await db.Sensors.Where(s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value)).ToListAsync(ct));
            db.WaterSystems.RemoveRange(await db.WaterSystems.Where(w => wsIds.Contains(w.Id)).ToListAsync(ct));
        }

        db.FarmingAreas.RemoveRange(demoAreas);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Cleared partial demo farm data ({N} areas).", demoAreas.Count);
    }
}
