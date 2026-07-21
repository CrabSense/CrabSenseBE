using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class HarvestReportService : IHarvestReportService
{
    private readonly IUnitOfWork _unitOfWork;

    public HarvestReportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<HarvestReportDto> GetHarvestReportAsync(
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

        // 3. Thống kê tổng quan
        var totalVouchers = vouchers.Count;

        var totalQuantity = vouchers.Sum(
            voucher => voucher.TotalQuantity);

        var totalWeightKg = vouchers.Sum(
            voucher => voucher.TotalWeightKg);

        // 4. Thống kê theo ngày
        var byDate = vouchers
            .GroupBy(voucher => voucher.HarvestDate.Date)
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
}