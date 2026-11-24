using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Quản lý phiếu thu hoạch, sản lượng và thống kê cua lột.
/// </summary>
public class HarvestService : IHarvestService
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;
    private readonly FarmAreaScope _areas;

    private readonly HarvestRecorder _harvestRecorder;

    public HarvestService(
        IUnitOfWork uow,
        ICurrentUserService currentUser,
        HarvestRecorder harvestRecorder)
    {
        _uow = uow;
        _currentUser = currentUser;
        _areas = new FarmAreaScope(uow, currentUser);
        _harvestRecorder = harvestRecorder;
    }

    public async Task<ApiResponse<HarvestVoucherDetailDto>> GetByIdAsync(
    Guid id,
    CancellationToken ct = default)
    {
        var voucher = await GetVoucherOrThrowAsync(id, ct);
        await EnsureVoucherAreaAsync(voucher, ct);

        var lines = await _uow.HarvestLines.FindAsync(
            line => line.HarvestVoucherId == id,
            ct);
        await EnsureVoucherLinesAreaAsync(voucher, lines, ct);
        var approvedLines = await ActiveApprovedLinesAsync(voucher, ct);

        return ApiResponse<HarvestVoucherDetailDto>.Ok(
            MapVoucherDetail(
                voucher, lines, await AreaNameAsync(voucher.FarmingAreaId, ct), approvedLines));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Query
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<HarvestVoucherDto>>> GetAllAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        var allowedAreas = await _areas.SelectionAsync(farmingAreaId, ct);
        var vouchers = (await _uow.HarvestVouchers.GetAllAsync(ct))
            .Where(voucher => allowedAreas is null
                || (voucher.FarmingAreaId is Guid areaId && allowedAreas.Contains(areaId)))
            .OrderByDescending(voucher => voucher.HarvestDate)
            .ThenByDescending(voucher => voucher.CreatedAt)
            .ToList();

        var voucherIds = vouchers
            .Select(voucher => voucher.Id)
            .ToList();

        var lines = voucherIds.Count == 0
            ? new List<HarvestLine>()
            : (
                await _uow.HarvestLines.FindAsync(
                    line => voucherIds.Contains(line.HarvestVoucherId),
                    ct))
                .ToList();

        var linesByVoucher = lines
            .GroupBy(line => line.HarvestVoucherId)
            .ToDictionary(
                group => group.Key,
                group => group.ToList());
        foreach (var voucher in vouchers)
        {
            var voucherLines = linesByVoucher.GetValueOrDefault(voucher.Id) ?? [];
            await EnsureVoucherLinesAreaAsync(voucher, voucherLines, ct);
        }

        var areaNames = await LoadAreaNamesAsync(
            vouchers.Select(v => v.FarmingAreaId),
            ct);

        var result = vouchers
            .Select(voucher =>
            {
                var voucherLines = linesByVoucher.TryGetValue(
                    voucher.Id,
                    out var foundLines)
                        ? foundLines
                        : Enumerable.Empty<HarvestLine>();

                return MapVoucher(
                    voucher,
                    voucherLines,
                    areaNames.GetValueOrDefault(voucher.FarmingAreaId ?? Guid.Empty));
            })
            .ToList();

        return ApiResponse<IEnumerable<HarvestVoucherDto>>.Ok(result);
    }

    public async Task<ApiResponse<HarvestOverviewDto>> GetOverviewAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        var allowedAreas = await _areas.SelectionAsync(farmingAreaId, ct);
        var boxIdsInArea = await BoxIdsInAreasAsync(allowedAreas, ct);
        var crabs = (await _uow.Crabs.GetAllAsync(ct)).ToList();
        bool InArea(Crab c) =>
            boxIdsInArea is null
            || (c.BoxId is Guid bid && boxIdsInArea.Contains(bid));

        var harvestableCrabs = crabs
            .Where(c =>
                (c.Status is CrabStatus.Alive or CrabStatus.Molting or CrabStatus.Quarantined)
                && InArea(c))
            .ToList();
        var harvestable = harvestableCrabs.Count;
        var softshellWaiting = harvestableCrabs.Count(c =>
            c.Condition is CrabCondition.Softshell or CrabCondition.Molting
            || c.Status == CrabStatus.Molting
            || c.MoltingStage.Contains("soft", StringComparison.OrdinalIgnoreCase)
            || c.MoltingStage.Contains("lột", StringComparison.OrdinalIgnoreCase));

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var areaVouchers = (await _uow.HarvestVouchers.GetAllAsync(ct))
            .Where(v => allowedAreas is null
                || (v.FarmingAreaId is Guid areaId && allowedAreas.Contains(areaId)))
            .ToList();
        var todayCompleted = areaVouchers
            .Where(v =>
                v.Status == HarvestStatus.Completed
                && v.HarvestDate >= today
                && v.HarvestDate < tomorrow)
            .ToList();
        var harvestedToday = todayCompleted.Sum(v => v.TotalQuantity);
        var todayWeightKg = decimal.Round(
            todayCompleted.Sum(v => v.TotalWeightKg),
            3);

        var completedIds = areaVouchers
            .Where(v => v.Status == HarvestStatus.Completed)
            .Select(v => v.Id)
            .ToHashSet();
        var harvestedCrabIds = completedIds.Count == 0
            ? new HashSet<Guid>()
            : (await _uow.HarvestLines.FindAsync(
                    l => l.CrabId != null && completedIds.Contains(l.HarvestVoucherId),
                    ct))
                .Select(l => l.CrabId!.Value)
                .ToHashSet();
        var waitingCrabs = crabs
            .Where(c => c.Status == CrabStatus.Harvested
                && (boxIdsInArea is null || harvestedCrabIds.Contains(c.Id)))
            .ToList();

        return ApiResponse<HarvestOverviewDto>.Ok(new HarvestOverviewDto(
            harvestable,
            harvestedToday,
            waitingCrabs.Count,
            todayWeightKg,
            softshellWaiting));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Create
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<HarvestVoucherDetailDto>> CreateAsync(
        CreateHarvestVoucherRequest request,
        CancellationToken ct = default)
    {
        var requestLines = request.Lines?.ToList() ?? [];

        if (request.HarvestDate == default)
        {
            throw AppException.BadRequest(
                "HarvestDate is required.");
        }

        if (request.HarvestDate.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5))
        {
            throw AppException.BadRequest(
                "HarvestDate cannot be in the future.");
        }

        var suppliedCrabIds = requestLines
            .Where(line => line.CrabId.HasValue)
            .Select(line => line.CrabId!.Value)
            .ToList();

        if (request.FarmingAreaId is null
            && suppliedCrabIds.Count == 0)
        {
            throw AppException.BadRequest(
                "FarmingAreaId is required when creating a harvest request without crabs.");
        }

        if (requestLines.Count > 0)
        {
            _harvestRecorder.ValidateHarvestLines(requestLines, requirePositiveWeight: false);
        }

        var areaId = request.FarmingAreaId;
        if (areaId is null && suppliedCrabIds.Count > 0)
            areaId = await InferAreaFromCrabAsync(suppliedCrabIds[0], ct);
        if (areaId is not Guid assignedAreaId || assignedAreaId == Guid.Empty)
            throw AppException.BadRequest("Không xác định được khu của phiếu thu hoạch.");
        await _areas.EnsureAsync(assignedAreaId, manage: false, ct);
        await _harvestRecorder.ValidateCrabsAsync(
            suppliedCrabIds, assignedAreaId, checkAlreadyHarvested: true,
            currentVoucherId: null, ct: ct);

        var performer = NormalizeNullable(request.PerformedByName)
            ?? await ResolveUserNameAsync(ct);

        var voucher = new HarvestVoucher
        {
            VoucherCode = await _harvestRecorder.GenerateVoucherCodeAsync(ct),
            HarvestDate = NormalizeUtc(request.HarvestDate),
            Status = HarvestStatus.PendingApproval,
            Notes = NormalizeNullable(request.Notes),
            CreatedBy = _currentUser.UserId,
            FarmingAreaId = areaId,
            PerformedByName = performer,
            PhotoUrlsJson = JsonStringList.Serialize(request.PhotoUrls)
        };
        //// Dùng trực tiếp HarvestLine đã được tạo và chuẩn hóa trong HarvestRecorder, tránh tạo trùng dữ liệu.
        foreach (var requestLine in requestLines)
        {
            var line = await _harvestRecorder.BuildLineAsync(
                voucher.Id,
                requestLine,
                ct);

            voucher.Lines.Add(line);
        }
        // foreach (var requestLine in requestLines)
        // {
        //     var snap = await _harvestRecorder.BuildLineAsync(voucher.Id, requestLine, ct);
        //     voucher.Lines.Add(new HarvestLine
        //     {
        //         HarvestVoucherId = voucher.Id,
        //         CrabId = requestLine.CrabId,
        //         BoxId = snap.BoxId,
        //         WeightGram = requestLine.WeightGram,
        //         Grade = NormalizeGrade(requestLine.Grade),
        //         IsSoftshell = requestLine.IsSoftshell,
        //         Notes = NormalizeNullable(requestLine.Notes),
        //         ConditionLabel = NormalizeNullable(requestLine.ConditionLabel),
        //         PhotoUrlsJson = JsonStringList.Serialize(requestLine.PhotoUrls),
        //         CrabCode = snap.CrabCode,
        //         AreaName = snap.AreaName,
        //         RowName = snap.RowName,
        //         BoxCode = snap.BoxCode,
        //         LotCode = snap.LotCode,
        //         Result = NormalizeResult(requestLine.Result)
        //     });
        // }

        voucher.TotalQuantity = voucher.Lines.Count;
        voucher.TotalWeightKg = CalculateTotalWeightKg(voucher.Lines);

        await _uow.HarvestVouchers.AddAsync(voucher, ct);

        await _uow.SaveChangesAsync(ct);

        return ApiResponse<HarvestVoucherDetailDto>.Ok(
            MapVoucherDetail(voucher, voucher.Lines, await AreaNameAsync(voucher.FarmingAreaId, ct)),
            $"Harvest voucher '{voucher.VoucherCode}' created.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Approval and execution workflow
    // ─────────────────────────────────────────────────────────────────────────

    public Task<ApiResponse<HarvestVoucherDetailDto>> ApproveAsync(
        Guid id, ApproveHarvestRequest request, CancellationToken ct = default)
        => DecideAsync(id, approved: true, request.Notes, ct);

    public Task<ApiResponse<HarvestVoucherDetailDto>> RejectAsync(
        Guid id, RejectHarvestRequest request, CancellationToken ct = default)
        => DecideAsync(id, approved: false, request.Reason, ct);

    private async Task<ApiResponse<HarvestVoucherDetailDto>> DecideAsync(
        Guid id, bool approved, string? note, CancellationToken ct)
    {
        var voucher = await GetVoucherOrThrowAsync(id, ct);
        await EnsureVoucherAreaAsync(voucher, ct);
        var lines = (await _uow.HarvestLines.FindAsync(
            line => line.HarvestVoucherId == id, ct)).ToList();
        await EnsureVoucherLinesAreaAsync(voucher, lines, ct);
        if (voucher.Status is not HarvestStatus.PendingApproval and not HarvestStatus.Planned)
            throw AppException.Conflict(
                $"Chỉ phiếu đang chờ duyệt mới có thể duyệt hoặc từ chối. Trạng thái hiện tại: '{voucher.Status}'.");
        if (voucher.CreatedBy == _currentUser.UserId
            || voucher.AdjustmentRequestedByUserId == _currentUser.UserId)
            throw AppException.Forbidden("Người tạo yêu cầu không được tự duyệt hoặc từ chối phiếu thu hoạch.");
        var normalized = NormalizeNullable(note);
        if (!approved && normalized is null)
            throw AppException.BadRequest("Cần ghi lý do từ chối thu hoạch.");

        if (approved && lines.Count == 0)
            throw AppException.BadRequest("Danh sách thu hoạch phải có ít nhất một dòng trước khi duyệt.");
        if (approved && voucher.FarmingAreaId is Guid areaId)
        {
            var crabIds = lines.Where(line => line.CrabId.HasValue)
                .Select(line => line.CrabId!.Value)
                .ToList();
            await _harvestRecorder.ValidateCrabsAsync(
                crabIds, areaId, checkAlreadyHarvested: true,
                currentVoucherId: voucher.Id, ct: ct);
        }

        var decidedAt = DateTime.UtcNow;
        if (approved)
        {
            voucher.ApprovalVersion++;
            voucher.ApprovedQuantity = lines.Count;
            foreach (var line in lines)
            {
                await _uow.HarvestApprovedLines.AddAsync(new HarvestApprovedLine
                {
                    HarvestVoucherId = voucher.Id,
                    ApprovalVersion = voucher.ApprovalVersion,
                    SourceHarvestLineId = line.Id,
                    CrabId = line.CrabId,
                    BoxId = line.BoxId,
                    FarmingAreaId = voucher.FarmingAreaId!.Value,
                    ApprovedByUserId = _currentUser.UserId,
                    ApprovedAt = decidedAt,
                    CrabCode = line.CrabCode,
                    BoxCode = line.BoxCode,
                    LotCode = line.LotCode
                }, ct);
            }
            voucher.ExecutionShortfallCount = 0;
            voucher.ExecutionShortfallReason = null;
        }

        voucher.Status = approved ? HarvestStatus.Approved : HarvestStatus.Rejected;
        voucher.ApprovedByUserId = _currentUser.UserId;
        voucher.ApprovedAt = decidedAt;
        voucher.RejectionReason = approved ? null : normalized;
        if (approved && normalized is not null)
            voucher.Notes = AppendNote(voucher.Notes, normalized);
        voucher.ConcurrencyVersion++;
        _uow.HarvestVouchers.Update(voucher);
        await _uow.SaveChangesAsync(ct);
        return await DetailResponseAsync(
            voucher,
            approved ? "Harvest request approved." : "Harvest request rejected.",
            ct);
    }

    public async Task<ApiResponse<HarvestVoucherDetailDto>> ExecuteAsync(
        Guid id, ExecuteHarvestRequest request, CancellationToken ct = default)
    {
        var voucher = await GetVoucherOrThrowAsync(id, ct);
        await EnsureVoucherAreaAsync(voucher, ct);
        await EnsureVoucherLinesAreaAsync(
            voucher,
            await _uow.HarvestLines.FindAsync(line => line.HarvestVoucherId == id, ct),
            ct);
        if (voucher.Status != HarvestStatus.Approved || voucher.ExecutionCompletedAt is not null)
            throw AppException.Conflict(
                $"Chỉ phiếu đã duyệt mới được thực hiện. Trạng thái hiện tại: '{voucher.Status}'.");

        var requestLines = request.Lines?.ToList() ?? [];
        if (requestLines.Count == 0)
            throw AppException.BadRequest("Cần nhập ít nhất một kết quả thu hoạch.");
        _harvestRecorder.ValidateHarvestLines(requestLines, requirePositiveWeight: true);
        var crabIds = requestLines
            .Where(line => line.CrabId.HasValue)
            .Select(line => line.CrabId!.Value)
            .ToList();
        if (voucher.FarmingAreaId is not Guid areaId)
            throw AppException.Conflict("Phiếu thu hoạch chưa gắn khu.");
        await _harvestRecorder.ValidateCrabsAsync(
            crabIds, areaId, checkAlreadyHarvested: true,
            currentVoucherId: voucher.Id, ct: ct);

        var approvedLines = (await _uow.HarvestApprovedLines.FindAsync(
            line => line.HarvestVoucherId == voucher.Id
                && line.ApprovalVersion == voucher.ApprovalVersion,
            ct)).ToList();
        if (approvedLines.Count == 0 || approvedLines.Count != voucher.ApprovedQuantity)
            throw AppException.Conflict("Không tìm thấy snapshot danh sách thu hoạch đã được duyệt.");

        EnsureExecutionMatchesApproval(approvedLines, requestLines);
        var shortfall = approvedLines.Count - requestLines.Count;
        var shortfallReason = NormalizeNullable(request.ShortfallReason);
        if (shortfall > 0 && shortfallReason is null)
            throw AppException.BadRequest("Kết quả thiếu cua so với danh sách duyệt; cần ghi lý do.");

        var oldLines = (await _uow.HarvestLines.FindAsync(
            line => line.HarvestVoucherId == voucher.Id, ct)).ToList();
        foreach (var line in oldLines)
            _uow.HarvestLines.Remove(line);
        voucher.Lines.Clear();
        //// Dùng trực tiếp HarvestLine đã được tạo và chuẩn hóa trong HarvestRecorder, tránh tạo trùng dữ liệu.
        foreach (var requestLine in requestLines)
        {
            var line = await _harvestRecorder.BuildLineAsync(
                voucher.Id,
                requestLine,
                ct);

            voucher.Lines.Add(line);
            await _uow.HarvestLines.AddAsync(line, ct);
        }
        // foreach (var requestLine in requestLines)
        // {
        //     var snap = await _harvestRecorder.BuildLineAsync(voucher.Id, requestLine, ct);
        //     var line = new HarvestLine
        //     {
        //         HarvestVoucherId = voucher.Id,
        //         CrabId = requestLine.CrabId,
        //         BoxId = snap.BoxId,
        //         WeightGram = requestLine.WeightGram,
        //         Grade = NormalizeGrade(requestLine.Grade),
        //         IsSoftshell = requestLine.IsSoftshell,
        //         Notes = NormalizeNullable(requestLine.Notes),
        //         ConditionLabel = NormalizeNullable(requestLine.ConditionLabel),
        //         PhotoUrlsJson = JsonStringList.Serialize(requestLine.PhotoUrls),
        //         CrabCode = snap.CrabCode,
        //         AreaName = snap.AreaName,
        //         RowName = snap.RowName,
        //         BoxCode = snap.BoxCode,
        //         LotCode = snap.LotCode,
        //         Result = NormalizeResult(requestLine.Result)
        //     };
        //     voucher.Lines.Add(line);
        //     await _uow.HarvestLines.AddAsync(line, ct);
        // }

        voucher.TotalQuantity = voucher.Lines.Count;
        voucher.TotalWeightKg = CalculateTotalWeightKg(voucher.Lines);
        voucher.Status = HarvestStatus.InProgress;
        voucher.PerformedByUserId = _currentUser.UserId;
        voucher.PerformedByName = await ResolveUserNameAsync(ct);
        voucher.ExecutionCompletedAt = DateTime.UtcNow;
        voucher.ExecutionShortfallCount = shortfall;
        voucher.ExecutionShortfallReason = shortfallReason;
        voucher.Notes = AppendNote(voucher.Notes, NormalizeNullable(request.Notes));
        if (request.PhotoUrls is not null)
            voucher.PhotoUrlsJson = JsonStringList.Serialize(request.PhotoUrls);
        voucher.ConcurrencyVersion++;
        _uow.HarvestVouchers.Update(voucher);
        await SaveWorkflowAsync(
            "Phiếu đã được một request khác cập nhật; vui lòng tải lại trước khi thực hiện.",
            ct);
        return await DetailResponseAsync(voucher, "Harvest execution recorded; waiting for manager confirmation.", ct);
    }

    public async Task<ApiResponse<HarvestVoucherDetailDto>> RequestAdjustmentAsync(
        Guid id,
        RequestHarvestAdjustmentRequest request,
        CancellationToken ct = default)
    {
        var voucher = await GetVoucherOrThrowAsync(id, ct);
        await EnsureVoucherAreaAsync(voucher, ct);
        if (voucher.Status != HarvestStatus.Approved || voucher.ExecutionCompletedAt is not null)
            throw AppException.Conflict("Chỉ phiếu đã duyệt nhưng chưa thực hiện mới được yêu cầu điều chỉnh.");
        var reason = NormalizeNullable(request.Reason)
            ?? throw AppException.BadRequest("Cần ghi lý do yêu cầu điều chỉnh.");
        var requestLines = request.Lines?.ToList() ?? [];
        if (requestLines.Count == 0)
            throw AppException.BadRequest("Danh sách điều chỉnh phải có ít nhất một dòng.");
        _harvestRecorder.ValidateHarvestLines(requestLines, requirePositiveWeight: false);
        if (voucher.FarmingAreaId is not Guid areaId)
            throw AppException.Conflict("Phiếu thu hoạch chưa gắn khu.");
        var crabIds = requestLines.Where(line => line.CrabId.HasValue)
            .Select(line => line.CrabId!.Value)
            .ToList();
        await _harvestRecorder.ValidateCrabsAsync(
            crabIds, areaId, checkAlreadyHarvested: true,
            currentVoucherId: voucher.Id, ct: ct);

        var oldLines = (await _uow.HarvestLines.FindAsync(
            line => line.HarvestVoucherId == voucher.Id, ct)).ToList();
        foreach (var line in oldLines)
            _uow.HarvestLines.Remove(line);
        voucher.Lines.Clear();
        foreach (var requestLine in requestLines)
        {
            var line = await _harvestRecorder.BuildLineAsync(voucher.Id, requestLine, ct);
            voucher.Lines.Add(line);
            await _uow.HarvestLines.AddAsync(line, ct);
        }

        voucher.TotalQuantity = voucher.Lines.Count;
        voucher.TotalWeightKg = CalculateTotalWeightKg(voucher.Lines);
        voucher.Status = HarvestStatus.PendingApproval;
        voucher.AdjustmentRequestedByUserId = _currentUser.UserId;
        voucher.AdjustmentRequestedAt = DateTime.UtcNow;
        voucher.AdjustmentReason = reason;
        voucher.ApprovedByUserId = null;
        voucher.ApprovedAt = null;
        voucher.ConcurrencyVersion++;
        _uow.HarvestVouchers.Update(voucher);
        await SaveWorkflowAsync(
            "Phiếu đã được một request khác cập nhật; vui lòng tải lại trước khi điều chỉnh.",
            ct);
        return await DetailResponseAsync(voucher, "Adjustment requested; manager approval is required again.", ct);
    }

    public async Task<ApiResponse<HarvestVoucherDetailDto>> ConfirmAsync(
        Guid id, ConfirmHarvestRequest request, CancellationToken ct = default)
    {
        var voucher = await GetVoucherOrThrowAsync(id, ct);
        await EnsureVoucherAreaAsync(voucher, ct);
        var actualLines = (await _uow.HarvestLines.FindAsync(line => line.HarvestVoucherId == id, ct)).ToList();
        await EnsureVoucherLinesAreaAsync(voucher, actualLines, ct);
        if (voucher.Status != HarvestStatus.InProgress || voucher.ExecutionCompletedAt is null)
            throw AppException.Conflict("Chỉ kết quả do Farm Staff đã thực hiện mới được xác nhận.");
        if (actualLines.Count == 0 || voucher.FarmingAreaId is not Guid areaId)
            throw AppException.Conflict("Không có kết quả thu hoạch hợp lệ để xác nhận.");
        await _harvestRecorder.ValidateCrabsAsync(actualLines.Where(x => x.CrabId.HasValue).Select(x => x.CrabId!.Value).ToList(),
            areaId, checkAlreadyHarvested: true, currentVoucherId: voucher.Id, ct: ct);
        await _harvestRecorder.ApplyHarvestAsync(voucher, actualLines, ct);
        voucher.Status = HarvestStatus.Completed;
        voucher.ConfirmedByUserId = _currentUser.UserId;
        voucher.ConfirmedAt = DateTime.UtcNow;
        voucher.Notes = AppendNote(voucher.Notes, NormalizeNullable(request.Notes));
        voucher.ConcurrencyVersion++;
        _uow.HarvestVouchers.Update(voucher);
        await SaveWorkflowAsync(
            "Phiếu đã được một request khác cập nhật; vui lòng tải lại trước khi xác nhận.",
            ct);
        return await DetailResponseAsync(voucher, "Harvest result confirmed.", ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Delete
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse> DeleteAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var voucher = await GetVoucherOrThrowAsync(id, ct);
        await EnsureVoucherAreaAsync(voucher, ct);
        await EnsureVoucherLinesAreaAsync(
            voucher,
            await _uow.HarvestLines.FindAsync(line => line.HarvestVoucherId == id, ct),
            ct);

        if (voucher.Status is not HarvestStatus.PendingApproval
            and not HarvestStatus.Planned
            and not HarvestStatus.Rejected
            and not HarvestStatus.Cancelled)
        {
            throw AppException.Conflict(
                "Only pending, rejected, planned or cancelled harvest vouchers can be deleted.");
        }

        var hasFrozenLots = await _uow.FrozenLots.AnyAsync(
            lot => lot.HarvestVoucherId == id,
            ct);

        if (hasFrozenLots)
        {
            throw AppException.Conflict(
                $"Cannot delete harvest voucher '{voucher.VoucherCode}' " +
                "because frozen lots reference it.");
        }

        var lines = await _uow.HarvestLines.FindAsync(
            line => line.HarvestVoucherId == id,
            ct);

        foreach (var line in lines)
        {
            _uow.HarvestLines.Remove(line);
        }

        _uow.HarvestVouchers.Remove(voucher);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse.Ok(
            $"Harvest voucher '{voucher.VoucherCode}' deleted.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Statistics
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<HarvestStatisticsDto>> GetStatisticsAsync(
        DateTime from,
        DateTime to,
        string period,
        CancellationToken ct = default)
    {
        var normalizedPeriod = NormalizePeriod(period);
        var fromUtc = NormalizeUtc(from);
        var toUtc = NormalizeUtc(to);

        if (from == default || to == default)
        {
            throw AppException.BadRequest(
                "Both from and to are required.");
        }

        if (toUtc <= fromUtc)
        {
            throw AppException.BadRequest(
                "'to' must be later than 'from'.");
        }

        var allowedAreas = await _areas.AreaIdsAsync(ct);
        var vouchers = (
            await _uow.HarvestVouchers.FindAsync(
                voucher =>
                    voucher.Status == HarvestStatus.Completed
                    && voucher.HarvestDate >= fromUtc
                    && voucher.HarvestDate < toUtc,
                ct))
            .Where(voucher => allowedAreas is null
                || (voucher.FarmingAreaId is Guid areaId && allowedAreas.Contains(areaId)))
            .ToList();

        var voucherIds = vouchers
            .Select(voucher => voucher.Id)
            .ToList();

        var lines = voucherIds.Count == 0
            ? new List<HarvestLine>()
            : (
                await _uow.HarvestLines.FindAsync(
                    line => voucherIds.Contains(line.HarvestVoucherId),
                    ct))
                .ToList();

        var linesByVoucher = lines
            .GroupBy(line => line.HarvestVoucherId)
            .ToDictionary(
                group => group.Key,
                group => group.ToList());

        var items = vouchers
            .GroupBy(voucher =>
                GetPeriodStart(voucher.HarvestDate, normalizedPeriod))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var periodVouchers = group.ToList();

                var periodLines = periodVouchers
                    .SelectMany(voucher =>
                        linesByVoucher.TryGetValue(voucher.Id, out var voucherLines)
                            ? voucherLines
                            : Enumerable.Empty<HarvestLine>())
                    .ToList();

                var softshellQuantity = periodLines.Count(
                    line => line.IsSoftshell);

                var totalQuantity = periodVouchers.Sum(
                    voucher => voucher.TotalQuantity);

                return new HarvestPeriodItemDto(
                    PeriodStart: group.Key,
                    PeriodEnd: GetPeriodEnd(group.Key, normalizedPeriod),
                    VoucherCount: periodVouchers.Count,
                    TotalQuantity: totalQuantity,
                    TotalWeightKg: decimal.Round(
                        periodVouchers.Sum(voucher => voucher.TotalWeightKg),
                        3),
                    SoftshellQuantity: softshellQuantity,
                    SoftshellRate: CalculateRate(
                        softshellQuantity,
                        totalQuantity));
            })
            .ToList();

        var totalSoftshellQuantity = lines.Count(line => line.IsSoftshell);
        var totalQuantity = vouchers.Sum(voucher => voucher.TotalQuantity);

        var result = new HarvestStatisticsDto(
            From: fromUtc,
            To: toUtc,
            Period: normalizedPeriod,
            VoucherCount: vouchers.Count,
            TotalQuantity: totalQuantity,
            TotalWeightKg: decimal.Round(
                vouchers.Sum(voucher => voucher.TotalWeightKg),
                3),
            SoftshellQuantity: totalSoftshellQuantity,
            SoftshellRate: CalculateRate(
                totalSoftshellQuantity,
                totalQuantity),
            Items: items);

        return ApiResponse<HarvestStatisticsDto>.Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Box ↔ Harvest link
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<HarvestBoxDto>>> GetBoxesByVoucherAsync(
        Guid voucherId,
        CancellationToken ct = default)
    {
        // Đảm bảo phiếu thu hoạch tồn tại
        var voucher = await GetVoucherOrThrowAsync(voucherId, ct);
        await EnsureVoucherAreaAsync(voucher, ct);

        var lines = await _uow.HarvestLines
            .Query()
            .Include(l => l.Box)
            .Where(l => l.HarvestVoucherId == voucherId)
            .ToListAsync(ct);

        // Nhóm theo BoxId → mỗi box một dòng
        var boxes = lines
            .Where(l => l.BoxId.HasValue)
            .GroupBy(l => l.BoxId!.Value)
            .Select(g => new HarvestBoxDto(
                BoxId: g.Key,
                BoxCode: g.First().Box?.Code ?? "N/A",
                CrabCount: g.Count(),
                TotalWeightGram: decimal.Round(g.Sum(l => l.WeightGram), 2)))
            .OrderBy(b => b.BoxCode)
            .ToList();

        return ApiResponse<IEnumerable<HarvestBoxDto>>.Ok(boxes);
    }

    public async Task<ApiResponse<IEnumerable<BoxHarvestVoucherDto>>> GetVouchersByBoxAsync(
        Guid boxId,
        CancellationToken ct = default)
    {
        // Đảm bảo box tồn tại
        _ = await _uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");
        await _areas.EnsureBoxAsync(boxId, manage: false, ct);

        var lines = await _uow.HarvestLines
            .Query()
            .Include(l => l.HarvestVoucher)
            .Where(l => l.BoxId == boxId)
            .ToListAsync(ct);

        // Nhóm theo VoucherId → mỗi phiếu thu hoạch một dòng
        var vouchers = lines
            .GroupBy(l => l.HarvestVoucherId)
            .Select(g =>
            {
                var voucher = g.First().HarvestVoucher!;
                return new BoxHarvestVoucherDto(
                    VoucherId: voucher.Id,
                    VoucherCode: voucher.VoucherCode,
                    HarvestDate: voucher.HarvestDate,
                    Status: voucher.Status.ToString(),
                    CrabCount: g.Count(),
                    TotalWeightGram: decimal.Round(g.Sum(l => l.WeightGram), 2));
            })
            .OrderByDescending(v => v.HarvestDate)
            .ToList();

        return ApiResponse<IEnumerable<BoxHarvestVoucherDto>>.Ok(vouchers);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Validation chuyển sang HarvestRecorder
    // ─────────────────────────────────────────────────────────────────────────


    // ─────────────────────────────────────────────────────────────────────────
    // Mapping
    // ─────────────────────────────────────────────────────────────────────────

    private static HarvestVoucherDto MapVoucher(
        HarvestVoucher voucher,
        IEnumerable<HarvestLine> lines,
        string? areaName = null)
    {
        var softshellQuantity = lines.Count(
            line => line.IsSoftshell);

        var lineDtos = lines.Select(MapLine).ToList();
        var passed = lineDtos.Count(l => IsPassed(l.Result));
        var failed = lineDtos.Count - passed;
        var avg = voucher.TotalQuantity <= 0
            ? 0
            : decimal.Round(voucher.TotalWeightKg * 1000m / voucher.TotalQuantity, 1);

        return new HarvestVoucherDto(
            Id: voucher.Id,
            VoucherCode: voucher.VoucherCode,
            HarvestDate: voucher.HarvestDate,
            Status: voucher.Status.ToString(),
            TotalQuantity: voucher.TotalQuantity,
            TotalWeightKg: voucher.TotalWeightKg,
            SoftshellQuantity: softshellQuantity,
            SoftshellRate: CalculateRate(
                softshellQuantity,
                voucher.TotalQuantity),
            Notes: voucher.Notes,
            CreatedBy: voucher.CreatedBy,
            CreatedAt: voucher.CreatedAt,
            FarmingAreaId: voucher.FarmingAreaId,
            PerformedByName: voucher.PerformedByName,
            AreaName: areaName,
            PassedCount: passed,
            FailedCount: failed,
            AverageWeightGram: avg,
            PhotoUrls: JsonStringList.Parse(voucher.PhotoUrlsJson),
            Lines: lineDtos,
            ApprovedByUserId: voucher.ApprovedByUserId,
            ApprovedAt: voucher.ApprovedAt,
            RejectionReason: voucher.RejectionReason,
            PerformedByUserId: voucher.PerformedByUserId,
            ExecutionCompletedAt: voucher.ExecutionCompletedAt,
            ConfirmedByUserId: voucher.ConfirmedByUserId,
            ConfirmedAt: voucher.ConfirmedAt,
            ApprovalVersion: voucher.ApprovalVersion,
            ApprovedQuantity: voucher.ApprovedQuantity,
            ExecutionShortfallCount: voucher.ExecutionShortfallCount,
            ExecutionShortfallReason: voucher.ExecutionShortfallReason,
            AdjustmentRequestedByUserId: voucher.AdjustmentRequestedByUserId,
            AdjustmentRequestedAt: voucher.AdjustmentRequestedAt,
            AdjustmentReason: voucher.AdjustmentReason);
    }

    private static HarvestVoucherDetailDto MapVoucherDetail(
        HarvestVoucher voucher,
        IEnumerable<HarvestLine> lines,
        string? areaName = null,
        IEnumerable<HarvestApprovedLine>? approvedLines = null)
    {
        var lineList = lines
            .OrderBy(line => line.CreatedAt)
            .Select(MapLine)
            .ToList();

        var softshellQuantity = lineList.Count(
            line => line.IsSoftshell);
        var passed = lineList.Count(l => IsPassed(l.Result));
        var failed = lineList.Count - passed;
        var avg = voucher.TotalQuantity <= 0
            ? 0
            : decimal.Round(voucher.TotalWeightKg * 1000m / voucher.TotalQuantity, 1);

        return new HarvestVoucherDetailDto(
            Id: voucher.Id,
            VoucherCode: voucher.VoucherCode,
            HarvestDate: voucher.HarvestDate,
            Status: voucher.Status.ToString(),
            TotalQuantity: voucher.TotalQuantity,
            TotalWeightKg: voucher.TotalWeightKg,
            SoftshellQuantity: softshellQuantity,
            SoftshellRate: CalculateRate(
                softshellQuantity,
                voucher.TotalQuantity),
            Notes: voucher.Notes,
            CreatedBy: voucher.CreatedBy,
            CreatedAt: voucher.CreatedAt,
            Lines: lineList,
            FarmingAreaId: voucher.FarmingAreaId,
            PerformedByName: voucher.PerformedByName,
            AreaName: areaName,
            PassedCount: passed,
            FailedCount: failed,
            AverageWeightGram: avg,
            PhotoUrls: JsonStringList.Parse(voucher.PhotoUrlsJson),
            ApprovedByUserId: voucher.ApprovedByUserId,
            ApprovedAt: voucher.ApprovedAt,
            RejectionReason: voucher.RejectionReason,
            PerformedByUserId: voucher.PerformedByUserId,
            ExecutionCompletedAt: voucher.ExecutionCompletedAt,
            ConfirmedByUserId: voucher.ConfirmedByUserId,
            ConfirmedAt: voucher.ConfirmedAt,
            ApprovalVersion: voucher.ApprovalVersion,
            ApprovedQuantity: voucher.ApprovedQuantity,
            ExecutionShortfallCount: voucher.ExecutionShortfallCount,
            ExecutionShortfallReason: voucher.ExecutionShortfallReason,
            AdjustmentRequestedByUserId: voucher.AdjustmentRequestedByUserId,
            AdjustmentRequestedAt: voucher.AdjustmentRequestedAt,
            AdjustmentReason: voucher.AdjustmentReason,
            ApprovedLines: (approvedLines ?? Enumerable.Empty<HarvestApprovedLine>())
                .OrderBy(line => line.CreatedAt)
                .Select(MapApprovedLine)
                .ToList());
    }

    private static HarvestLineDto MapLine(
        HarvestLine line)
    {
        return new HarvestLineDto(
            Id: line.Id,
            CrabId: line.CrabId,
            BoxId: line.BoxId,
            BoxCode: line.BoxCode ?? line.Box?.Code,
            WeightGram: line.WeightGram,
            Grade: line.Grade,
            CarapaceLengthMm: line.CarapaceLengthMm,
            CarapaceWidthMm: line.CarapaceWidthMm,
            HarvestedAt: line.HarvestedAt,
            IsSoftshell: line.IsSoftshell,
            Notes: line.Notes,
            CrabCode: line.CrabCode,
            ConditionLabel: line.ConditionLabel,
            PhotoUrls: JsonStringList.Parse(line.PhotoUrlsJson),
            AreaName: line.AreaName,
            RowName: line.RowName,
            LotCode: line.LotCode,
            Result: line.Result);
    }

    private static HarvestApprovedLineDto MapApprovedLine(HarvestApprovedLine line) =>
        new(
            line.Id,
            line.ApprovalVersion,
            line.CrabId,
            line.BoxId,
            line.FarmingAreaId,
            line.ApprovedByUserId,
            line.ApprovedAt,
            line.CrabCode,
            line.BoxCode,
            line.LotCode);

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<HarvestVoucher> GetVoucherOrThrowAsync(
        Guid id,
        CancellationToken ct)
    {
        return await _uow.HarvestVouchers.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("HarvestVoucher");
    }

    private async Task EnsureVoucherAreaAsync(HarvestVoucher voucher, CancellationToken ct)
    {
        if (voucher.FarmingAreaId is not Guid areaId || areaId == Guid.Empty)
            throw AppException.Forbidden("Phiếu thu hoạch chưa gắn khu được phân công.");
        await _areas.EnsureAsync(areaId, manage: false, ct);
    }

    private async Task EnsureVoucherLinesAreaAsync(
        HarvestVoucher voucher,
        IEnumerable<HarvestLine> lines,
        CancellationToken ct)
    {
        if (voucher.FarmingAreaId is not Guid voucherAreaId)
            throw AppException.Forbidden("Phiếu thu hoạch chưa gắn khu được phân công.");
        foreach (var line in lines.Where(item => item.CrabId.HasValue))
        {
            Guid? lineAreaId = null;
            if (line.BoxId is Guid boxId)
            {
                var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
                if (box is not null)
                {
                    var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
                    lineAreaId = row?.FarmingAreaId;
                }
            }
            if (lineAreaId is null && line.CrabId is Guid crabId)
            {
                var crab = await _uow.Crabs.GetByIdAsync(crabId, ct);
                if (crab is not null)
                    lineAreaId = await _harvestRecorder.AreaOfCrabAsync(crab, ct);
            }
            if (lineAreaId != voucherAreaId)
                throw AppException.Forbidden(
                    $"Cua '{line.CrabId}' không thuộc khu của phiếu thu hoạch.");
        }
    }

    private async Task<ApiResponse<HarvestVoucherDetailDto>> DetailResponseAsync(
        HarvestVoucher voucher, string message, CancellationToken ct)
    {
        var lines = await _uow.HarvestLines.FindAsync(
            line => line.HarvestVoucherId == voucher.Id, ct);
        var lineList = lines.ToList();
        if (lineList.Count == 0 && voucher.Lines.Count > 0)
            lineList = voucher.Lines.ToList();
        var approvedLines = await ActiveApprovedLinesAsync(voucher, ct);
        return ApiResponse<HarvestVoucherDetailDto>.Ok(
            MapVoucherDetail(
                voucher,
                lineList,
                await AreaNameAsync(voucher.FarmingAreaId, ct),
                approvedLines),
            message);
    }

    private async Task<IReadOnlyCollection<HarvestApprovedLine>> ActiveApprovedLinesAsync(
        HarvestVoucher voucher,
        CancellationToken ct)
    {
        if (voucher.ApprovalVersion <= 0)
            return Array.Empty<HarvestApprovedLine>();
        return (await _uow.HarvestApprovedLines.FindAsync(
            line => line.HarvestVoucherId == voucher.Id
                && line.ApprovalVersion == voucher.ApprovalVersion,
            ct)).ToList();
    }

    private static string NormalizeResult(string? raw)
    {
        var key = (raw ?? "passed").Trim().ToLowerInvariant();
        if (key is "failed" or "fail" or "khongdat" or "không đạt" or "reject")
            return "failed";
        return "passed";
    }

    private static bool IsPassed(string? result) =>
        !string.Equals(result, "failed", StringComparison.OrdinalIgnoreCase);

    private static void EnsureExecutionMatchesApproval(
        IReadOnlyCollection<HarvestApprovedLine> approvedLines,
        IReadOnlyCollection<HarvestLineRequest> requestLines)
    {
        if (requestLines.Count > approvedLines.Count)
            throw AppException.Conflict("Kết quả thực hiện vượt quá số lượng đã được duyệt.");

        var approvedCrabIds = approvedLines
            .Where(line => line.CrabId.HasValue)
            .Select(line => line.CrabId!.Value)
            .ToHashSet();
        var unassignedApprovedCount = approvedLines.Count(line => line.CrabId is null);
        var unassignedRequestCount = requestLines.Count(line => line.CrabId is null);
        if (unassignedRequestCount > unassignedApprovedCount)
            throw AppException.Conflict("Kết quả có thêm dòng ngoài phạm vi đã được duyệt.");

        var outside = requestLines
            .Where(line => line.CrabId.HasValue && !approvedCrabIds.Contains(line.CrabId.Value))
            .Select(line => line.CrabId!.Value)
            .FirstOrDefault();
        if (outside != Guid.Empty)
            throw AppException.Forbidden(
                $"Cua '{outside}' không nằm trong danh sách đã được Farm Manager duyệt.");
    }


    private async Task SaveWorkflowAsync(string concurrencyMessage, CancellationToken ct)
    {
        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict(concurrencyMessage);
        }
    }

    private static decimal CalculateTotalWeightKg(
        IEnumerable<HarvestLine> lines)
    {
        return decimal.Round(
            lines.Sum(line => line.WeightGram) / 1000m,
            3,
            MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateRate(
        int softshellQuantity,
        int totalQuantity)
    {
        if (totalQuantity <= 0)
        {
            return 0m;
        }

        return decimal.Round(
            softshellQuantity * 100m / totalQuantity,
            2,
            MidpointRounding.AwayFromZero);
    }

    private static string NormalizePeriod(
        string period)
    {
        if (string.IsNullOrWhiteSpace(period))
        {
            throw AppException.BadRequest(
                "Period is required. Allowed values: day, week, month.");
        }

        var normalized = period.Trim().ToLowerInvariant();

        if (normalized is not "day"
            and not "week"
            and not "month")
        {
            throw AppException.BadRequest(
                "Invalid period. Allowed values: day, week, month.");
        }

        return normalized;
    }

    private static DateTime GetPeriodStart(
        DateTime value,
        string period)
    {
        var date = value.Date;

        return period switch
        {
            "day" => DateTime.SpecifyKind(
                date,
                DateTimeKind.Utc),

            "week" => DateTime.SpecifyKind(
                date.AddDays(
                    -(((int)date.DayOfWeek + 6) % 7)),
                DateTimeKind.Utc),

            "month" => new DateTime(
                date.Year,
                date.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc),

            _ => throw AppException.BadRequest(
                "Invalid statistics period.")
        };
    }

    private static DateTime GetPeriodEnd(
        DateTime periodStart,
        string period)
    {
        return period switch
        {
            "day" => periodStart.AddDays(1),
            "week" => periodStart.AddDays(7),
            "month" => periodStart.AddMonths(1),
            _ => throw AppException.BadRequest(
                "Invalid statistics period.")
        };
    }

    private static DateTime NormalizeUtc(
        DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string? NormalizeNullable(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? NormalizeGrade(
        string? grade)
    {
        return string.IsNullOrWhiteSpace(grade)
            ? null
            : grade.Trim().ToUpperInvariant();
    }


    private async Task<Guid?> InferAreaFromCrabAsync(Guid crabId, CancellationToken ct)
    {
        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct);
        if (crab?.BoxId is not Guid boxId) return null;
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
        if (box is null) return null;
        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
        return row?.FarmingAreaId;
    }

    private async Task<string?> AreaNameAsync(Guid? areaId, CancellationToken ct)
    {
        if (areaId is not Guid id || id == Guid.Empty) return null;
        var area = await _uow.FarmingAreas.GetByIdAsync(id, ct);
        if (area is null) return null;
        return string.IsNullOrWhiteSpace(area.Name) ? area.Code : area.Name;
    }

    private async Task<Dictionary<Guid, string>> LoadAreaNamesAsync(
        IEnumerable<Guid?> ids,
        CancellationToken ct)
    {
        var set = ids.Where(id => id is Guid g && g != Guid.Empty)
            .Select(id => id!.Value)
            .ToHashSet();
        if (set.Count == 0) return new Dictionary<Guid, string>();
        var areas = await _uow.FarmingAreas.GetAllAsync(ct);
        return areas
            .Where(a => set.Contains(a.Id))
            .ToDictionary(
                a => a.Id,
                a => string.IsNullOrWhiteSpace(a.Name) ? a.Code : a.Name);
    }

    private async Task<HashSet<Guid>?> BoxIdsInAreasAsync(
        HashSet<Guid>? farmingAreaIds, CancellationToken ct)
    {
        if (farmingAreaIds is null) return null;
        var rowIds = (await _uow.FarmingRows.GetAllAsync(ct))
            .Where(row => farmingAreaIds.Contains(row.FarmingAreaId))
            .Select(r => r.Id)
            .ToHashSet();
        return (await _uow.Boxes.GetAllAsync(ct))
            .Where(b => rowIds.Contains(b.FarmingRowId))
            .Select(b => b.Id)
            .ToHashSet();
    }

    private static string? AppendNote(string? current, string? addition)
    {
        if (addition is null) return current;
        return string.IsNullOrWhiteSpace(current)
            ? addition
            : $"{current}{Environment.NewLine}{addition}";
    }

    private async Task<string> ResolveUserNameAsync(CancellationToken ct)
    {
        try
        {
            var user = await _uow.Users.GetByIdAsync(_currentUser.UserId, ct);
            if (user is not null && !string.IsNullOrWhiteSpace(user.FullName))
                return user.FullName.Trim();
        }
        catch
        {
            // JWT without user row
        }

        return "Owner";
    }
}