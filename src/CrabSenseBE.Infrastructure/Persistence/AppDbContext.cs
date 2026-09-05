using CrabSenseBE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Auth
    public DbSet<AppUser> AppUsers => Set<AppUser>();

    // Farm
    public DbSet<FarmingArea> FarmingAreas => Set<FarmingArea>();
    public DbSet<FarmingRow> FarmingRows => Set<FarmingRow>();
    public DbSet<Box> Boxes => Set<Box>();
    public DbSet<Crab> Crabs => Set<Crab>();
    public DbSet<CrabLot> CrabLots => Set<CrabLot>();
    // public DbSet<CropBatch> CropBatches => Set<CropBatch>();
    public DbSet<OperationLog> OperationLogs => Set<OperationLog>();
    public DbSet<CrabBoxAllocation> CrabBoxAllocations => Set<CrabBoxAllocation>();
    public DbSet<MoltingRecord> MoltingRecords => Set<MoltingRecord>();
    public DbSet<BoxStatusHistory> BoxStatusHistories => Set<BoxStatusHistory>();
    public DbSet<CrabMortalityRecord> CrabMortalityRecords=> Set<CrabMortalityRecord>();
    public DbSet<CrabStatusHistory> CrabStatusHistories => Set<CrabStatusHistory>();
    public DbSet<CrabWeightHistory> CrabWeightHistories => Set<CrabWeightHistory>();
    public DbSet<CrabAiAnalysis> CrabAiAnalyses => Set<CrabAiAnalysis>();
    public DbSet<CrabHarvestHistory> CrabHarvestHistories => Set<CrabHarvestHistory>();

    // IoT
    public DbSet<WaterSystem> WaterSystems => Set<WaterSystem>();
    public DbSet<RasComponent> RasComponents => Set<RasComponent>();
    public DbSet<WaterFlow> WaterFlows => Set<WaterFlow>();
    public DbSet<Sensor> Sensors => Set<Sensor>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<WaterMeasurement> WaterMeasurements => Set<WaterMeasurement>();
    public DbSet<Hdf5Upload> Hdf5Uploads => Set<Hdf5Upload>();

    // Alerts
    public DbSet<AlertThreshold> AlertThresholds => Set<AlertThreshold>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<UserPushToken> UserPushTokens => Set<UserPushToken>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    // Harvest / Frozen / QR
    public DbSet<HarvestVoucher> HarvestVouchers => Set<HarvestVoucher>();
    public DbSet<HarvestLine> HarvestLines => Set<HarvestLine>();
    public DbSet<FrozenLot> FrozenLots => Set<FrozenLot>();
    public DbSet<QrCode> QrCodes => Set<QrCode>();
    public DbSet<TraceabilityLink> TraceabilityLinks => Set<TraceabilityLink>();

    // Sales / Market
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<FarmSettings> FarmSettings => Set<FarmSettings>();

    // AI
    public DbSet<AiDetection> AiDetections => Set<AiDetection>();
    public DbSet<AiFeedback> AiFeedbacks => Set<AiFeedback>();
    public DbSet<AiRecommendation> AiRecommendations => Set<AiRecommendation>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<FarmOperation> FarmOperations => Set<FarmOperation>();
    public DbSet<SaleTransaction> SaleTransactions => Set<SaleTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema riêng — không đụng bảng Cap_Ras snake_case trong public
        modelBuilder.HasDefaultSchema("be");

        // Apply all IEntityTypeConfiguration from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        // Enum conversions stored as string
        modelBuilder.Entity<AppUser>()
            .Property(u => u.Role)
            .HasConversion<string>();

        modelBuilder.Entity<Alert>()
            .Property(a => a.Severity)
            .HasConversion<string>();

        modelBuilder.Entity<Alert>()
            .Property(a => a.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Device>()
            .Property(d => d.Status)
            .HasConversion<string>();

        modelBuilder.Entity<SalesOrder>()
            .Property(o => o.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Payment>()
            .Property(p => p.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Invoice>()
            .Property(i => i.PaymentStatus)
            .HasConversion<string>();

        modelBuilder.Entity<FrozenLot>()
            .Property(f => f.Status)
            .HasConversion<string>();

        modelBuilder.Entity<HarvestVoucher>()
            .Property(h => h.Status)
            .HasConversion<string>();

        modelBuilder.Entity<FarmingArea>()
            .Property(a => a.Status)
            .HasConversion<string>();

        modelBuilder.Entity<FarmingArea>()
            .Property(a => a.Code)
            .HasMaxLength(32);

        modelBuilder.Entity<FarmingArea>()
            .Property(a => a.Location)
            .HasMaxLength(256);

        modelBuilder.Entity<FarmingArea>()
            .HasIndex(a => a.Code)
            .IsUnique();

        modelBuilder.Entity<FarmingArea>()
            .Property(a => a.AreaSquareMeters)
            .HasPrecision(12, 2);

        modelBuilder.Entity<FarmingRow>()
            .Property(r => r.Status)
            .HasConversion<string>();

        modelBuilder.Entity<FarmingRow>()
            .Property(r => r.Code)
            .HasMaxLength(32);

        modelBuilder.Entity<FarmingRow>()
            .Property(r => r.Location)
            .HasMaxLength(256);

        modelBuilder.Entity<FarmingRow>()
            .HasIndex(r => r.Code)
            .IsUnique();

        modelBuilder.Entity<Crab>()
            .Property(c => c.Code)
            .HasMaxLength(32);
        modelBuilder.Entity<Crab>()
            .HasIndex(c => c.Code)
            .IsUnique();
        modelBuilder.Entity<Crab>()
            .Property(c => c.QrCode)
            .HasMaxLength(48);
        modelBuilder.Entity<Crab>()
            .Property(c => c.Condition)
            .HasConversion<string>();
        modelBuilder.Entity<Crab>()
            .Property(c => c.Gender)
            .HasConversion<string>();
        modelBuilder.Entity<Crab>()
            .Property(c => c.WeightGram)
            .HasPrecision(10, 2);
        modelBuilder.Entity<Crab>()
            .Property(c => c.InitialWeightGram)
            .HasPrecision(10, 2);
        modelBuilder.Entity<Crab>()
            .Property(c => c.CarapaceWidthMm)
            .HasPrecision(8, 2);
        modelBuilder.Entity<Crab>()
            .Property(c => c.CarapaceLengthMm)
            .HasPrecision(8, 2);
        modelBuilder.Entity<Crab>()
            .Property(c => c.AiConfidence)
            .HasPrecision(5, 2);

        modelBuilder.Entity<CrabLot>()
            .Property(l => l.LotCode)
            .HasMaxLength(32);
        modelBuilder.Entity<CrabLot>()
            .HasIndex(l => l.LotCode)
            .IsUnique();
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.Name)
            .HasMaxLength(128);
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.Condition)
            .HasMaxLength(16);
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.Status)
            .HasMaxLength(16);
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.TotalWeightKg)
            .HasPrecision(12, 3);
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.AverageWeightGram)
            .HasPrecision(10, 2);
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.WeightMinGram)
            .HasPrecision(10, 2);
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.WeightMaxGram)
            .HasPrecision(10, 2);
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.UnitPriceVndPerKg)
            .HasPrecision(14, 2);
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.CrabCostVnd)
            .HasPrecision(14, 2);
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.ShippingCostVnd)
            .HasPrecision(14, 2);
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.OtherCostVnd)
            .HasPrecision(14, 2);
        modelBuilder.Entity<CrabLot>()
            .Property(l => l.TotalCostVnd)
            .HasPrecision(14, 2);

        modelBuilder.Entity<CrabStatusHistory>()
            .Property(h => h.OldCondition)
            .HasConversion<string>();
        modelBuilder.Entity<CrabStatusHistory>()
            .Property(h => h.NewCondition)
            .HasConversion<string>();
        modelBuilder.Entity<CrabStatusHistory>()
            .Property(h => h.OldStatus)
            .HasConversion<string>();
        modelBuilder.Entity<CrabStatusHistory>()
            .Property(h => h.NewStatus)
            .HasConversion<string>();

        modelBuilder.Entity<CrabWeightHistory>()
            .Property(h => h.WeightGram)
            .HasPrecision(10, 2);
        modelBuilder.Entity<CrabAiAnalysis>()
            .Property(h => h.Confidence)
            .HasPrecision(5, 2);
        modelBuilder.Entity<CrabHarvestHistory>()
            .Property(h => h.WeightGram)
            .HasPrecision(10, 2);

        modelBuilder.Entity<UserPushToken>()
            .HasIndex(t => new { t.UserId, t.Token })
            .IsUnique();

        modelBuilder.Entity<WaterSystem>()
            .Property(w => w.Status)
            .HasMaxLength(32);
        modelBuilder.Entity<WaterSystem>()
            .Property(w => w.FlowStatus)
            .HasMaxLength(32);

        modelBuilder.Entity<RasComponent>()
            .Property(c => c.Code)
            .HasMaxLength(64);
        modelBuilder.Entity<RasComponent>()
            .Property(c => c.Name)
            .HasMaxLength(128);
        modelBuilder.Entity<RasComponent>()
            .Property(c => c.Type)
            .HasMaxLength(32);
        modelBuilder.Entity<RasComponent>()
            .Property(c => c.Status)
            .HasMaxLength(32);
        modelBuilder.Entity<RasComponent>()
            .Property(c => c.Capacity)
            .HasPrecision(12, 2);
        modelBuilder.Entity<RasComponent>()
            .HasIndex(c => new { c.WaterSystemId, c.Position });
        modelBuilder.Entity<RasComponent>()
            .HasOne(c => c.WaterSystem)
            .WithMany(w => w.Components)
            .HasForeignKey(c => c.WaterSystemId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RasComponent>()
            .HasOne(c => c.RelayDevice)
            .WithMany()
            .HasForeignKey(c => c.RelayDeviceId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<WaterFlow>()
            .Property(f => f.FlowRate)
            .HasPrecision(12, 2);
        modelBuilder.Entity<WaterFlow>()
            .Property(f => f.Status)
            .HasMaxLength(32);
        modelBuilder.Entity<WaterFlow>()
            .HasOne(f => f.WaterSystem)
            .WithMany(w => w.Flows)
            .HasForeignKey(f => f.WaterSystemId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WaterFlow>()
            .HasOne(f => f.FromComponent)
            .WithMany(c => c.OutgoingFlows)
            .HasForeignKey(f => f.FromComponentId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<WaterFlow>()
            .HasOne(f => f.ToComponent)
            .WithMany(c => c.IncomingFlows)
            .HasForeignKey(f => f.ToComponentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Sensor>()
            .HasOne(s => s.RasComponent)
            .WithMany(c => c.Sensors)
            .HasForeignKey(s => s.RasComponentId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Auto-set UpdatedAt on modified entities
        foreach (var entry in ChangeTracker.Entries<Domain.Common.BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
