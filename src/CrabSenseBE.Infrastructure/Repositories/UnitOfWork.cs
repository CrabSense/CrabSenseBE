using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;

namespace CrabSenseBE.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    // Auth
    private IRepository<AppUser>? _users;
    public IRepository<AppUser> Users => _users ??= new GenericRepository<AppUser>(_context);

    // Farm
    private IRepository<FarmingArea>? _farmingAreas;
    public IRepository<FarmingArea> FarmingAreas => _farmingAreas ??= new GenericRepository<FarmingArea>(_context);

    private IRepository<FarmingRow>? _farmingRows;
    public IRepository<FarmingRow> FarmingRows => _farmingRows ??= new GenericRepository<FarmingRow>(_context);

    private IRepository<Box>? _boxes;
    public IRepository<Box> Boxes => _boxes ??= new GenericRepository<Box>(_context);

    private IRepository<Crab>? _crabs;
    public IRepository<Crab> Crabs => _crabs ??= new GenericRepository<Crab>(_context);

    private IRepository<CrabLot>? _crabLots;
    public IRepository<CrabLot> CrabLots => _crabLots ??= new GenericRepository<CrabLot>(_context);

    private IRepository<CropBatch>? _cropBatches;
    public IRepository<CropBatch> CropBatches => _cropBatches ??= new GenericRepository<CropBatch>(_context);

    private IRepository<OperationLog>? _operationLogs;
    public IRepository<OperationLog> OperationLogs => _operationLogs ??= new GenericRepository<OperationLog>(_context);

    private IRepository<CrabBoxAllocation>? _crabBoxAllocations;
    public IRepository<CrabBoxAllocation> CrabBoxAllocations =>
        _crabBoxAllocations ??= new GenericRepository<CrabBoxAllocation>(_context);

    private IRepository<MoltingRecord>? _moltingRecords;
    public IRepository<MoltingRecord> MoltingRecords =>
        _moltingRecords ??= new GenericRepository<MoltingRecord>(_context);

    private IRepository<BoxStatusHistory>? _boxStatusHistories;
    public IRepository<BoxStatusHistory> BoxStatusHistories =>
        _boxStatusHistories ??= new GenericRepository<BoxStatusHistory>(_context);

    // IoT
    private IRepository<WaterSystem>? _waterSystems;
    public IRepository<WaterSystem> WaterSystems => _waterSystems ??= new GenericRepository<WaterSystem>(_context);

    private IRepository<Sensor>? _sensors;
    public IRepository<Sensor> Sensors => _sensors ??= new GenericRepository<Sensor>(_context);

    private IRepository<Device>? _devices;
    public IRepository<Device> Devices => _devices ??= new GenericRepository<Device>(_context);

    private IRepository<WaterMeasurement>? _waterMeasurements;
    public IRepository<WaterMeasurement> WaterMeasurements => _waterMeasurements ??= new GenericRepository<WaterMeasurement>(_context);

    private IRepository<Hdf5Upload>? _hdf5Uploads;
    public IRepository<Hdf5Upload> Hdf5Uploads => _hdf5Uploads ??= new GenericRepository<Hdf5Upload>(_context);

    // Alerts
    private IRepository<AlertThreshold>? _alertThresholds;
    public IRepository<AlertThreshold> AlertThresholds => _alertThresholds ??= new GenericRepository<AlertThreshold>(_context);

    private IRepository<Alert>? _alerts;
    public IRepository<Alert> Alerts => _alerts ??= new GenericRepository<Alert>(_context);

    private IRepository<Notification>? _notifications;
    public IRepository<Notification> Notifications => _notifications ??= new GenericRepository<Notification>(_context);

    private IRepository<NotificationChannel>? _notificationChannels;
    public IRepository<NotificationChannel> NotificationChannels =>
        _notificationChannels ??= new GenericRepository<NotificationChannel>(_context);

    private IRepository<NotificationDelivery>? _notificationDeliveries;
    public IRepository<NotificationDelivery> NotificationDeliveries =>
        _notificationDeliveries ??= new GenericRepository<NotificationDelivery>(_context);

    private IRepository<UserPushToken>? _userPushTokens;
    public IRepository<UserPushToken> UserPushTokens =>
        _userPushTokens ??= new GenericRepository<UserPushToken>(_context);

    private IRepository<MediaAsset>? _mediaAssets;
    public IRepository<MediaAsset> MediaAssets =>
        _mediaAssets ??= new GenericRepository<MediaAsset>(_context);

    // Harvest / Frozen / QR
    private IRepository<HarvestVoucher>? _harvestVouchers;
    public IRepository<HarvestVoucher> HarvestVouchers => _harvestVouchers ??= new GenericRepository<HarvestVoucher>(_context);

    private IRepository<HarvestLine>? _harvestLines;
    public IRepository<HarvestLine> HarvestLines => _harvestLines ??= new GenericRepository<HarvestLine>(_context);

    private IRepository<FrozenLot>? _frozenLots;
    public IRepository<FrozenLot> FrozenLots => _frozenLots ??= new GenericRepository<FrozenLot>(_context);

    private IRepository<QrCode>? _qrCodes;
    public IRepository<QrCode> QrCodes => _qrCodes ??= new GenericRepository<QrCode>(_context);

    private IRepository<TraceabilityLink>? _traceabilityLinks;
    public IRepository<TraceabilityLink> TraceabilityLinks => _traceabilityLinks ??= new GenericRepository<TraceabilityLink>(_context);

    // Sales
    private IRepository<Customer>? _customers;
    public IRepository<Customer> Customers => _customers ??= new GenericRepository<Customer>(_context);

    private IRepository<PriceList>? _priceLists;
    public IRepository<PriceList> PriceLists => _priceLists ??= new GenericRepository<PriceList>(_context);

    private IRepository<SalesOrder>? _salesOrders;
    public IRepository<SalesOrder> SalesOrders => _salesOrders ??= new GenericRepository<SalesOrder>(_context);

    private IRepository<SalesOrderLine>? _salesOrderLines;
    public IRepository<SalesOrderLine> SalesOrderLines => _salesOrderLines ??= new GenericRepository<SalesOrderLine>(_context);

    private IRepository<Delivery>? _deliveries;
    public IRepository<Delivery> Deliveries => _deliveries ??= new GenericRepository<Delivery>(_context);

    private IRepository<Payment>? _payments;
    public IRepository<Payment> Payments => _payments ??= new GenericRepository<Payment>(_context);

    private IRepository<Invoice>? _invoices;
    public IRepository<Invoice> Invoices => _invoices ??= new GenericRepository<Invoice>(_context);

    private IRepository<FarmSettings>? _farmSettings;
    public IRepository<FarmSettings> FarmSettings => _farmSettings ??= new GenericRepository<FarmSettings>(_context);

    // AI
    private IRepository<AiDetection>? _aiDetections;
    public IRepository<AiDetection> AiDetections => _aiDetections ??= new GenericRepository<AiDetection>(_context);

    private IRepository<AiFeedback>? _aiFeedbacks;
    public IRepository<AiFeedback> AiFeedbacks => _aiFeedbacks ??= new GenericRepository<AiFeedback>(_context);

    private IRepository<AiRecommendation>? _aiRecommendations;
    public IRepository<AiRecommendation> AiRecommendations => _aiRecommendations ??= new GenericRepository<AiRecommendation>(_context);

    private IRepository<Inspection>? _inspections;
    public IRepository<Inspection> Inspections => _inspections ??= new GenericRepository<Inspection>(_context);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    public void Dispose() => _context.Dispose();
}
