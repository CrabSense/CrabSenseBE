using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Application.Common;


namespace CrabSenseBE.Application.Services;

public class HarvestReportService : IHarvestReportService
{
    private readonly IUnitOfWork _unitOfWork;

    public HarvestReportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<HarvestReportDto> GetHarvestReportAsync(
        HarvestReportFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        // 1. Lấy các phiếu thu hoạch đã hoàn thành
        var completedVouchers =
            await _unitOfWork.HarvestVouchers.FindAsync(
                voucher => voucher.Status == HarvestStatus.Completed,
                cancellationToken);

        var vouchers = completedVouchers.ToList();

        // 2. Lấy tất cả dòng thu hoạch thuộc các voucher đã hoàn thành
        var voucherIds = vouchers
            .Select(v => v.Id)
            .ToHashSet();

        var allLines =
            await _unitOfWork.HarvestLines.GetAllAsync(
                cancellationToken);

        var lines = allLines
            .Where(line => voucherIds.Contains(line.HarvestVoucherId))
            .ToList();

        // Lọc theo lô cua nhập nếu client truyền CrabLotId.
        if (filter.CrabLotId.HasValue)
        {
            var crabIdsInLot = (await _unitOfWork.Crabs.FindAsync(
                    crab => crab.CrabLotId == filter.CrabLotId.Value,
                    cancellationToken))
                .Select(crab => crab.Id)
                .ToHashSet();

            // Chỉ giữ các dòng xác định được cua và cua thuộc lô đã chọn.
            lines = lines
                .Where(line => line.CrabId.HasValue
                            && crabIdsInLot.Contains(line.CrabId.Value))
                .ToList();

            // Chỉ giữ phiếu có ít nhất một dòng thuộc lô.
            var matchingVoucherIds = lines
                .Select(line => line.HarvestVoucherId)
                .ToHashSet();

            vouchers = vouchers
                .Where(voucher => matchingVoucherIds.Contains(voucher.Id))
                .ToList();
        }

        // 3. Thống kê tổng quan
        //Khi không chọn lô, báo cáo giữ nguyên tổng cũ. Khi chọn lô, tổng được tính từ các dòng đã lọc.
        var totalVouchers = vouchers.Count;
        var totalQuantity = filter.CrabLotId.HasValue
            ? lines.Count
            : vouchers.Sum(voucher => voucher.TotalQuantity);

        var totalWeightKg = filter.CrabLotId.HasValue
            ? lines.Sum(line => line.WeightGram) / 1000m
            : vouchers.Sum(voucher => voucher.TotalWeightKg);

        // 4. Thống kê theo ngày
        var voucherDateById = vouchers.ToDictionary(
        voucher => voucher.Id,
        voucher => voucher.HarvestDate.Date);

        List<HarvestByDateDto> byDate;

        if (filter.CrabLotId.HasValue)
        {
            byDate = lines
                .GroupBy(line => voucherDateById[line.HarvestVoucherId])
                .OrderBy(group => group.Key)
                .Select(group => new HarvestByDateDto
                {
                    Date = group.Key,
                    Quantity = group.Count(),
                    WeightKg = group.Sum(line => line.WeightGram) / 1000m
                })
                .ToList();
        }
        else
        {
            byDate = vouchers.GroupBy(voucher => voucher.HarvestDate.Date)
                    .OrderBy(group => group.Key)
                    .Select(group => new HarvestByDateDto
                    {
                        Date = group.Key,

                        Quantity = group.Sum(
                            voucher => voucher.TotalQuantity),

                        WeightKg = group.Sum(
                            voucher => voucher.TotalWeightKg)
                    })
                    .ToList();
        }

        // 5. Thống kê theo Grade
        var byGrade = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Grade))
            .GroupBy(line => line.Grade!)
            .OrderBy(group => group.Key)
            .Select(group => new HarvestByGradeDto
            {
                Grade = group.Key,

                WeightKg = group.Sum(
                    line => line.WeightGram) / 1000m
            })
            .ToList();

        // 6. Thống kê cua softshell
        var softshellWeightKg = lines
            .Where(line => line.IsSoftshell)
            .Sum(line => line.WeightGram) / 1000m;

        // 7. Trả kết quả
        return new HarvestReportDto
        {
            TotalVouchers = totalVouchers,

            TotalQuantity = totalQuantity,

            TotalWeightKg = totalWeightKg,

            ByDate = byDate,

            ByGrade = byGrade,

            Softshell = new HarvestSoftshellDto
            {
                WeightKg = softshellWeightKg
            }
        };
    }


    /// <summary>
    /// Báo cáo sản lượng theo ngày, tuần hoặc tháng.
    ///
    /// API sử dụng:
    /// GET /api/reports/harvest/period
    /// </summary>
    public async Task<HarvestPeriodReportDto> GetHarvestReportByPeriodAsync(
        HarvestReportFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        await ValidateFilter(filter, cancellationToken);

        // Lấy các phiếu thu hoạch đã hoàn thành.
        var completedVouchers =
            await _unitOfWork.HarvestVouchers.FindAsync(
                voucher => voucher.Status == HarvestStatus.Completed,
                cancellationToken);

        IEnumerable<Domain.Entities.HarvestVoucher> vouchers =
            completedVouchers;
        
        // Lọc theo khu nuôi nếu client truyền farmingAreaId.
        if (filter.FarmingAreaId.HasValue)
        {
            vouchers = vouchers.Where(voucher =>
                voucher.FarmingAreaId == filter.FarmingAreaId.Value);
        }

        // Lọc từ ngày bắt đầu.
        if (filter.FromDate.HasValue)
        {
            var fromDate = filter.FromDate.Value.Date;

            vouchers = vouchers.Where(voucher =>
                voucher.HarvestDate >= fromDate);
        }

        // Lọc đến ngày kết thúc.
        // Dùng mốc ngày kế tiếp để bao phủ toàn bộ ngày ToDate.
        if (filter.ToDate.HasValue)
        {
            var toDateExclusive =
                filter.ToDate.Value.Date.AddDays(1);

            vouchers = vouchers.Where(voucher =>
                voucher.HarvestDate < toDateExclusive);
        }

        var voucherList = vouchers.ToList();

        // Lấy các dòng chi tiết của các phiếu sau khi đã lọc.
        var voucherIds = voucherList
            .Select(voucher => voucher.Id)
            .ToHashSet();

        var lines = (await _unitOfWork.HarvestLines.FindAsync(
            line => voucherIds.Contains(line.HarvestVoucherId),
            cancellationToken)).ToList();

        // Lọc theo lô cua nhập nếu client truyền CrabLotId.
        if (filter.CrabLotId.HasValue)
        {
            var crabIdsInLot = (await _unitOfWork.Crabs.FindAsync(
                    crab => crab.CrabLotId == filter.CrabLotId.Value,
                    cancellationToken))
                .Select(crab => crab.Id)
                .ToHashSet();

            // Chỉ giữ dòng thu hoạch có cua thuộc lô đang chọn.
            lines = lines
                .Where(line => line.CrabId.HasValue
                            && crabIdsInLot.Contains(line.CrabId.Value))
                .ToList();

            // Chỉ giữ phiếu có ít nhất một dòng thuộc lô.
            var matchingVoucherIds = lines
                .Select(line => line.HarvestVoucherId)
                .ToHashSet();

            voucherList = voucherList
                .Where(voucher => matchingVoucherIds.Contains(voucher.Id))
                .ToList();
        }

        // Gom phiếu theo ngày, tuần hoặc tháng.
        // Gom sản lượng theo ngày, tuần hoặc tháng.
        var byPeriod = voucherList
            .GroupBy(voucher =>
                GetPeriodStart(voucher.HarvestDate, filter.Period))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var periodStart = group.Key;

                // Tìm các dòng thuộc những phiếu trong kỳ hiện tại.
                var periodVoucherIds = group
                    .Select(voucher => voucher.Id)
                    .ToHashSet();

                var periodLines = lines
                    .Where(line => periodVoucherIds.Contains(line.HarvestVoucherId))
                    .ToList();

                return new HarvestPeriodItemDto
                {
                    PeriodStart = periodStart,
                    PeriodEnd = GetPeriodEnd(periodStart, filter.Period),

                    // VoucherCount vẫn đếm phiếu có ít nhất một dòng thuộc lô.
                    VoucherCount = group.Count(),

                    // Khi lọc lô, chỉ tính các dòng cua thuộc lô đã chọn.
                    // Không lọc lô thì giữ tổng số liệu gốc của cả phiếu.
                    Quantity = filter.CrabLotId.HasValue
                        ? periodLines.Count
                        : group.Sum(voucher => voucher.TotalQuantity),

                    WeightKg = filter.CrabLotId.HasValue
                        ? periodLines.Sum(line => line.WeightGram) / 1000m
                        : group.Sum(voucher => voucher.TotalWeightKg)
                };
            })
            .ToList();

        // Gom khối lượng theo grade.
        var byGrade = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Grade))
            .GroupBy(line => line.Grade!)
            .OrderBy(group => group.Key)
            .Select(group => new HarvestByGradeDto
            {
                Grade = group.Key,
                WeightKg = group.Sum(
                    line => line.WeightGram) / 1000m
            })
            .ToList();

        // Tổng khối lượng cua softshell.
        var softshellWeightKg = lines
            .Where(line => line.IsSoftshell)
            .Sum(line => line.WeightGram) / 1000m;

        return new HarvestPeriodReportDto
        {
            TotalVouchers = voucherList.Count,

            TotalQuantity = filter.CrabLotId.HasValue
                ? lines.Count
                : voucherList.Sum(voucher => voucher.TotalQuantity),

            TotalWeightKg = filter.CrabLotId.HasValue
                ? lines.Sum(line => line.WeightGram) / 1000m
                : voucherList.Sum(voucher => voucher.TotalWeightKg),

            SoftshellWeightKg = softshellWeightKg,

            Period = filter.Period,

            FromDate = filter.FromDate,

            ToDate = filter.ToDate,

            FarmingAreaId = filter.FarmingAreaId,

            ByPeriod = byPeriod,

            ByGrade = byGrade
        };
    }

    /// <summary>
    /// Kiểm tra tính hợp lệ của bộ lọc.
    /// </summary>
    private async Task ValidateFilter(
        HarvestReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        if (filter.FromDate.HasValue &&
            filter.ToDate.HasValue &&
            filter.FromDate.Value.Date >
            filter.ToDate.Value.Date)
        {
            throw AppException.BadRequest(
                "FromDate cannot be greater than ToDate.");
        }

        if (!Enum.IsDefined(filter.Period))
        {
            throw AppException.BadRequest(
                "Period must be Day, Week or Month.");
        }

        if (filter.FarmingAreaId.HasValue)
        {
            var areaExists = await _unitOfWork.FarmingAreas.AnyAsync(
                a => a.Id == filter.FarmingAreaId.Value,
                cancellationToken);

            if (!areaExists)
            {
                throw AppException.BadRequest(
                    "FarmingAreaId does not exist.");
            }
        }

        if (filter.FromDate.HasValue && filter.ToDate.HasValue)
        {
            var rangeDays = (filter.ToDate.Value.Date
                - filter.FromDate.Value.Date).Days;

            if (rangeDays > 365)
            {
                throw AppException.BadRequest(
                    "Date range cannot exceed 365 days.");
            }
        }

        // Kiểm tra lô cua (nếu có) trước khi dùng làm bộ lọc.
        if (filter.CrabLotId.HasValue)
        {
            var lotExists = await _unitOfWork.CrabLots.AnyAsync(
                lot => lot.Id == filter.CrabLotId.Value,
                cancellationToken);

            if (!lotExists)
                throw AppException.BadRequest("CrabLotId does not exist.");
        }

    }

    /// <summary>
    /// Xác định ngày bắt đầu của nhóm dữ liệu.
    /// </summary>
    private static DateTime GetPeriodStart(
        DateTime date,
        HarvestReportPeriod period)
    {
        var dateOnly = date.Date;

        return period switch
        {
            HarvestReportPeriod.Day =>
                dateOnly,

            HarvestReportPeriod.Week =>
                GetMonday(dateOnly),

            HarvestReportPeriod.Month =>
                new DateTime(
                    dateOnly.Year,
                    dateOnly.Month,
                    1),

            _ => throw new ArgumentOutOfRangeException(
                nameof(period),
                period,
                "Unsupported harvest report period.")
        };
    }

    /// <summary>
    /// Xác định ngày kết thúc của nhóm.
    ///
    /// Giá trị trả về là thời điểm cuối ngày của period.
    /// </summary>
    private static DateTime GetPeriodEnd(
        DateTime periodStart,
        HarvestReportPeriod period)
    {
        return period switch
        {
            HarvestReportPeriod.Day =>
                periodStart
                    .AddDays(1)
                    .AddTicks(-1),

            HarvestReportPeriod.Week =>
                periodStart
                    .AddDays(7)
                    .AddTicks(-1),

            HarvestReportPeriod.Month =>
                periodStart
                    .AddMonths(1)
                    .AddTicks(-1),

            _ => throw new ArgumentOutOfRangeException(
                nameof(period),
                period,
                "Unsupported harvest report period.")
        };
    }

    /// <summary>
    /// Lấy thứ Hai của tuần chứa ngày được truyền vào.
    /// </summary>
    private static DateTime GetMonday(DateTime date)
    {
        var daysSinceMonday =
            ((int)date.DayOfWeek + 6) % 7;

        return date
            .AddDays(-daysSinceMonday)
            .Date;
    }
}