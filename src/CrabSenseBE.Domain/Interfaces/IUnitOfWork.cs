using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.Domain.Interfaces;

/// <summary>Unit of Work pattern — wraps all repositories + SaveChanges</summary>
public interface IUnitOfWork : IDisposable
{
    IRepository<AppUser> Users { get; }
    IRepository<FarmingArea> FarmingAreas { get; }
    IRepository<FarmingRow> FarmingRows { get; }
    IRepository<Box> Boxes { get; }
    IRepository<Crab> Crabs { get; }
    IRepository<CrabLot> CrabLots { get; }
    // IRepository<CropBatch> CropBatches { get; }
    IRepository<CrabMortalityRecord> CrabMortalityRecords { get; }
    IRepository<OperationLog> OperationLogs { get; }
    IRepository<CrabBoxAllocation> CrabBoxAllocations { get; }
    IRepository<MoltingRecord> MoltingRecords { get; }
    IRepository<BoxStatusHistory> BoxStatusHistories { get; }
    IRepository<WaterSystem> WaterSystems { get; }
    IRepository<Sensor> Sensors { get; }
    IRepository<Device> Devices { get; }
    IRepository<WaterMeasurement> WaterMeasurements { get; }
    IRepository<Hdf5Upload> Hdf5Uploads { get; }
    IRepository<AlertThreshold> AlertThresholds { get; }
    IRepository<Alert> Alerts { get; }
    IRepository<Notification> Notifications { get; }
    IRepository<NotificationChannel> NotificationChannels { get; }
    IRepository<NotificationDelivery> NotificationDeliveries { get; }
    IRepository<UserPushToken> UserPushTokens { get; }
    IRepository<MediaAsset> MediaAssets { get; }
    IRepository<HarvestVoucher> HarvestVouchers { get; }
    IRepository<HarvestLine> HarvestLines { get; }
    IRepository<FrozenLot> FrozenLots { get; }
    IRepository<QrCode> QrCodes { get; }
    IRepository<TraceabilityLink> TraceabilityLinks { get; }
    IRepository<Customer> Customers { get; }
    IRepository<PriceList> PriceLists { get; }
    IRepository<SalesOrder> SalesOrders { get; }
    IRepository<SalesOrderLine> SalesOrderLines { get; }
    IRepository<Delivery> Deliveries { get; }
    IRepository<Payment> Payments { get; }
    IRepository<Invoice> Invoices { get; }
    IRepository<FarmSettings> FarmSettings { get; }
    IRepository<AiDetection> AiDetections { get; }
    IRepository<AiFeedback> AiFeedbacks { get; }
    IRepository<AiRecommendation> AiRecommendations { get; }
    IRepository<Inspection> Inspections { get; }
    IRepository<FarmOperation> FarmOperations { get; }
    IRepository<SaleTransaction> SaleTransactions { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    Task<List<CrabMortalityRecord>> GetMortalityRecordsWithDetailsAsync(CancellationToken ct = default);
    Task<Crab?> GetCrabWithDetailsAsync(Guid crabId,CancellationToken cancellationToken = default);
}
