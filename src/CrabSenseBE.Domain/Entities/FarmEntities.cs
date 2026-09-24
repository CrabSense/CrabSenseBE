using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Khu nuôi (Area) — thuộc Owner. Hierachy: Owner → Khu → Dãy → Hộp → Cua.</summary>
public class FarmingArea : BaseEntity
{
    /// <summary>Chủ sở hữu khu — gắn từ JWT lúc tạo.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Mã khu hệ thống: AREA-A01, AREA-A02… Không sửa từ client.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    /// <summary>Vị trí trong cơ sở (vd. Nhà nuôi số 1 - Tầng 1).</summary>
    public string? Location { get; set; }
    /// <summary>Giữ cột cũ — không bắt buộc trên form khu.</summary>
    public string Address { get; set; } = string.Empty;
    public string? Region { get; set; }
    /// <summary>Diện tích khu (m²).</summary>
    public decimal? AreaSquareMeters { get; set; }
    public DateTime? EstablishedAt { get; set; }
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public FarmStatus Status { get; set; } = FarmStatus.Active;
    /// <summary>Đồng bộ từ Status == Active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Ảnh bản đồ (Maps nhìn từ trên) của trại — dùng làm nền "Bản đồ trại". Null = FE dùng ảnh mặc định.</summary>
    public string? MapImageUrl { get; set; }
    /// <summary>Khung khu trên ảnh bản đồ, tỉ lệ 0–1 theo chiều rộng/cao ảnh. Null = FE tự neo.</summary>
    public decimal? MapX1 { get; set; }
    public decimal? MapY1 { get; set; }
    public decimal? MapX2 { get; set; }
    public decimal? MapY2 { get; set; }

    /// <summary>Vĩ độ GPS (WGS84). Null = chưa ghim bản đồ.</summary>
    public double? Latitude { get; set; }
    /// <summary>Kinh độ GPS (WGS84). Null = chưa ghim bản đồ.</summary>
    public double? Longitude { get; set; }

    // Navigation
    public AppUser? Owner { get; set; }
    public ICollection<FarmingRow> Rows { get; set; } = new List<FarmingRow>();
    public ICollection<WaterSystem> WaterSystems { get; set; } = new List<WaterSystem>();
}

/// <summary>Dãy nuôi — thuộc Khu. Chứa hộp; sức chứa = số hộp tối đa.</summary>
public class FarmingRow : BaseEntity
{
    public Guid FarmingAreaId { get; set; }
    /// <summary>Mã dãy hệ thống: DAY-A01, DAY-A02… Không sửa từ client.</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Vị trí trong khu (vd. Bên trái, Dãy số 01).</summary>
    public string? Location { get; set; }
    public string? Description { get; set; }
    /// <summary>Số hộp tối đa. 0 = không giới hạn.</summary>
    public int Capacity { get; set; }
    /// <summary>Thứ tự hiển thị trong khu. 0 = theo mã.</summary>
    public int SortOrder { get; set; }
    public FarmStatus Status { get; set; } = FarmStatus.Active;
    public bool IsActive { get; set; } = true;

    /// <summary>Tâm dãy trên ảnh bản đồ trại, tỉ lệ 0–1. Null = chưa đặt vị trí.</summary>
    public decimal? MapX { get; set; }
    public decimal? MapY { get; set; }

    // Navigation
    public FarmingArea? FarmingArea { get; set; }
    public ICollection<Box> Boxes { get; set; } = new List<Box>();
}

/// <summary>Hộp/Khay nuôi cua — MOD-FARM</summary>
public class Box : BaseEntity
{
    public Guid FarmingRowId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Status { get; set; }
    public bool IsOccupied { get; set; } = false;

    /// <summary>Tâm hộp trên ảnh bản đồ trại, tỉ lệ 0–1. Null = chưa đặt vị trí.</summary>
    public decimal? MapX { get; set; }
    public decimal? MapY { get; set; }

    // Navigation
    public FarmingRow? FarmingRow { get; set; }
    public ICollection<Crab> Crabs { get; set; } = new List<Crab>();
}

/// <summary>Cá thể cua — snapshot hiện tại. Lịch sử nằm bảng riêng.</summary>
public class Crab : BaseEntity
{
    public Guid? BoxId { get; set; }
    public Guid CrabLotId { get; set; }

    /// <summary>Mã cua hệ thống: CRAB-0001… Không sửa từ client.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Tem QR: QR-CRAB-0001.</summary>
    public string? QrCode { get; set; }
    /// <summary>Alias hiển thị — mặc định = Code.</summary>
    public string? Tag { get; set; }
    /// <summary>Loại cua, vd. Cua biển.</summary>
    public string? CrabType { get; set; }
    public CrabGender Gender { get; set; } = CrabGender.Unknown;

    /// <summary>Trọng lượng hiện tại (g). Ban đầu copy sang InitialWeightGram.</summary>
    public decimal? WeightGram { get; set; }
    public decimal? InitialWeightGram { get; set; }
    /// <summary>Bề rộng mai (mm).</summary>
    public decimal? CarapaceWidthMm { get; set; }
    /// <summary>Bề ngang mai (mm).</summary>
    public decimal? CarapaceLengthMm { get; set; }
    /// <summary>Tình trạng lúc thả, vd. Khỏe mạnh.</summary>
    public string? InitialCondition { get; set; }
    public string? Notes { get; set; }

    public DateTime StockedAt { get; set; }
    public string MoltingStage { get; set; } = string.Empty;
    public DateTime? MoltedAt { get; set; }
    public CrabStatus Status { get; set; } = CrabStatus.Alive;
    /// <summary>Trạng thái Owner: Normal / Premolt / Molting / Softshell / Problem / Dead / Harvested.</summary>
    public CrabCondition Condition { get; set; } = CrabCondition.Normal;

    /// <summary>Snapshot AI mới nhất — lịch sử ở CrabAiAnalyses.</summary>
    public string? AiPrediction { get; set; }
    public decimal? AiConfidence { get; set; }

    /// <summary>JSON array of public S3 (or media) image URLs. Default [].</summary>
    public string ImageUrlsJson { get; set; } = "[]";

    // Navigation
    public Box? Box { get; set; }
    public CrabLot? CrabLot { get; set; }
    public ICollection<CrabBoxAllocation> BoxAllocations { get; set; }
        = new List<CrabBoxAllocation>();
    public ICollection<MoltingRecord> MoltingRecords { get; set; }
        = new List<MoltingRecord>();
    public ICollection<CrabMortalityRecord> MortalityRecords { get; set; }
        = new List<CrabMortalityRecord>();
    public ICollection<CrabStatusHistory> StatusHistories { get; set; }
        = new List<CrabStatusHistory>();
    public ICollection<CrabWeightHistory> WeightHistories { get; set; }
        = new List<CrabWeightHistory>();
    public ICollection<CrabAiAnalysis> AiAnalyses { get; set; }
        = new List<CrabAiAnalysis>();
    public ICollection<CrabHarvestHistory> HarvestHistories { get; set; }
        = new List<CrabHarvestHistory>();
}

/// <summary>Lô cua — phiếu nhập hàng. Cá thể cua gắn CrabLotId, theo dõi riêng.</summary>
public class CrabLot : BaseEntity
{
    /// <summary>LOT-20260905-001 — hệ thống tự cấp.</summary>
    public string LotCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime ImportDate { get; set; }
    /// <summary>Số con khai báo lúc nhập (không ghi đè bằng số cua đã thả).</summary>
    public int Quantity { get; set; }
    public string? SupplierName { get; set; }

    public decimal? TotalWeightKg { get; set; }
    public decimal? AverageWeightGram { get; set; }
    public decimal? WeightMinGram { get; set; }
    public decimal? WeightMaxGram { get; set; }

    public decimal? UnitPriceVndPerKg { get; set; }
    public decimal? CrabCostVnd { get; set; }
    public decimal? ShippingCostVnd { get; set; }
    public decimal? OtherCostVnd { get; set; }
    public decimal? TotalCostVnd { get; set; }

    /// <summary>Good | Average | Problem</summary>
    public string Condition { get; set; } = "Good";
    /// <summary>Pending | Allocating | Completed | Cancelled. Chỉ persist Cancelled; còn lại suy ra từ số đã thả.</summary>
    public string Status { get; set; } = "Pending";
    public int DeadOnArrival { get; set; }
    public string? Notes { get; set; }
    /// <summary>JSON array URL ảnh lô nhập (Google Drive).</summary>
    public string ImageUrlsJson { get; set; } = "[]";

    public ICollection<Crab> Crabs { get; set; } = new List<Crab>();
}

/// <summary>Đợt nuôi (CropBatch) — theo vụ</summary>
// public class CropBatch : BaseEntity
// {
//     public string BatchCode { get; set; } = string.Empty;
//     public DateTime StartDate { get; set; }
//     public DateTime? EndDate { get; set; }
//     /// <summary> Tổng số cua được thả ban đầu vào đợt nuôi.</summary>
//     public int InitialQuantity { get; set; }
//     public string? Status { get; set; }
//     public string? Notes { get; set; }

//     public ICollection<Crab> Crabs { get; set; } = new List<Crab>();
//     public ICollection<CrabMortalityRecord> MortalityRecords { get; set; }
//         = new List<CrabMortalityRecord>();
// }

/// <summary>Nhật ký vận hành — MOD-FARM</summary>
public class OperationLog : BaseEntity
{
    public Guid UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }

    // Navigation
    public AppUser? User { get; set; }
}

/// <summary>
/// Ghi nhận số lượng cua chết.
/// Dùng để tính tỷ lệ sống/chết theo:
/// - Đợt nuôi
/// - Khu
/// - Dãy
/// - Box
/// - Thời gian
/// - Nguyên nhân
/// </summary>
public class CrabMortalityRecord : BaseEntity
{
    /// <summary>
    /// Cá thể cua chết.
    /// Mỗi CrabId tương ứng với một con cua.
    /// </summary>
    public Guid CrabId { get; set; }

    /// <summary>
    /// Thời điểm ghi nhận cua chết.
    /// </summary>
    public DateTime MortalityDate { get; set; }

    /// <summary>
    /// Nguyên nhân cua chết.
    /// </summary>
    public MortalityCause Cause { get; set; } = MortalityCause.Unknown;

    /// <summary>
    /// Ghi chú.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Người ghi nhận.
    /// </summary>
    public Guid RecordedBy { get; set; }

    // Navigation
    public Crab? Crab { get; set; }

    public AppUser? Recorder { get; set; }
}