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
    public DbSet<CropBatch> CropBatches => Set<CropBatch>();
    public DbSet<OperationLog> OperationLogs => Set<OperationLog>();
    public DbSet<CrabBoxAllocation> CrabBoxAllocations => Set<CrabBoxAllocation>();
    public DbSet<MoltingRecord> MoltingRecords => Set<MoltingRecord>();
    public DbSet<BoxStatusHistory> BoxStatusHistories => Set<BoxStatusHistory>();

    // IoT
    public DbSet<WaterSystem> WaterSystems => Set<WaterSystem>();
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

        modelBuilder.Entity<UserPushToken>()
            .HasIndex(t => new { t.UserId, t.Token })
            .IsUnique();
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
