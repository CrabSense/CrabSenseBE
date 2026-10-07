namespace CrabSenseBE.Application.DTOs.Reports;

/// <summary>
/// Kiểu thời gian dùng để gom nhóm báo cáo thu hoạch.
/// </summary>
public enum HarvestReportPeriod
{
    /// <summary>Gom sản lượng theo ngày.</summary>
    Day,

    /// <summary>Gom sản lượng theo tuần, bắt đầu từ thứ Hai.</summary>
    Week,

    /// <summary>Gom sản lượng theo tháng.</summary>
    Month
}

/// <summary>
/// Bộ lọc cho báo cáo sản lượng thu hoạch.
/// </summary>
public sealed record HarvestReportFilterDto
{
    /// <summary>
    /// Ngày bắt đầu. Được tính từ 00:00:00.
    /// </summary>
    public DateTime? FromDate { get; init; }

    /// <summary>
    /// Ngày kết thúc. Được tính bao gồm toàn bộ ngày đó.
    /// </summary>
    public DateTime? ToDate { get; init; }

    /// <summary>
    /// Cách gom dữ liệu: Day, Week hoặc Month.
    /// </summary>
    public HarvestReportPeriod Period { get; init; }
        = HarvestReportPeriod.Day;

    /// <summary>
    /// Lọc theo khu nuôi.
    /// Null nghĩa là lấy toàn bộ khu.
    /// </summary>
    public Guid? FarmingAreaId { get; init; }
}

/// <summary>
/// Một dòng dữ liệu sản lượng của một ngày, tuần hoặc tháng.
/// </summary>
public sealed record HarvestPeriodItemDto
{
    /// <summary>Thời điểm bắt đầu kỳ.</summary>
    public DateTime PeriodStart { get; init; }

    /// <summary>Thời điểm kết thúc kỳ.</summary>
    public DateTime PeriodEnd { get; init; }

    /// <summary>Số phiếu thu hoạch trong kỳ.</summary>
    public int VoucherCount { get; init; }

    /// <summary>Số lượng cua thu hoạch trong kỳ.</summary>
    public int Quantity { get; init; }

    /// <summary>Tổng khối lượng cua, đơn vị kg.</summary>
    public decimal WeightKg { get; init; }
}

/// <summary>
/// Kết quả báo cáo sản lượng thu hoạch theo kỳ.
/// </summary>
public sealed record HarvestPeriodReportDto
{
    /// <summary>Tổng số phiếu thu hoạch đã hoàn thành.</summary>
    public int TotalVouchers { get; init; }

    /// <summary>Tổng số cua đã thu hoạch.</summary>
    public int TotalQuantity { get; init; }

    /// <summary>Tổng khối lượng cua, đơn vị kg.</summary>
    public decimal TotalWeightKg { get; init; }

    /// <summary>Tổng khối lượng cua softshell, đơn vị kg.</summary>
    public decimal SoftshellWeightKg { get; init; }

    /// <summary>Cách gom dữ liệu.</summary>
    public HarvestReportPeriod Period { get; init; }

    /// <summary>Ngày bắt đầu bộ lọc.</summary>
    public DateTime? FromDate { get; init; }

    /// <summary>Ngày kết thúc bộ lọc.</summary>
    public DateTime? ToDate { get; init; }

    /// <summary>Khu nuôi được lọc.</summary>
    public Guid? FarmingAreaId { get; init; }

    /// <summary>Sản lượng được gom theo ngày, tuần hoặc tháng.</summary>
    public List<HarvestPeriodItemDto> ByPeriod { get; init; } = [];

    /// <summary>Khối lượng phân loại theo grade.</summary>
    public List<HarvestByGradeDto> ByGrade { get; init; } = [];
}