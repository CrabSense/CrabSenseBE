namespace CrabSenseBE.Application.DTOs.Harvest;

// --- HarvestVoucher ---
public record HarvestVoucherDto(
    Guid Id, string VoucherCode, Guid? CropBatchId,
    DateTime HarvestDate, string Status, int TotalQuantity, decimal TotalWeightKg, string? Notes
);
public record CreateHarvestVoucherRequest(
    Guid? CropBatchId, DateTime HarvestDate, string? Notes,
    IEnumerable<HarvestLineRequest> Lines
);
public record HarvestLineRequest(Guid? CrabId, decimal WeightGram, string? Grade, bool IsSoftshell);

// --- FrozenLot ---
public record FrozenLotDto(
    Guid Id, string LotCode, Guid? HarvestVoucherId,
    DateTime FrozenDate, DateTime ExpiryDate, decimal WeightKg, string? Grade,
    int Quantity, string Status, string? StorageLocation
);
public record CreateFrozenLotRequest(
    Guid? HarvestVoucherId, DateTime FrozenDate, DateTime ExpiryDate,
    decimal WeightKg, string? Grade, int Quantity, string? StorageLocation
);

// --- QR ---
public record QrCodeDto(Guid Id, string Code, Guid? FrozenLotId, int ScanCount, DateTime? ExpiresAt);
public record GenerateQrRequest(Guid? FrozenLotId, Guid? HarvestVoucherId, DateTime? ExpiresAt);
public record TraceabilityPublicDto(
    string Code, string? FrozenLotCode, string? HarvestVoucherCode,
    DateTime? FrozenDate, DateTime? HarvestDate, string? Grade
);
