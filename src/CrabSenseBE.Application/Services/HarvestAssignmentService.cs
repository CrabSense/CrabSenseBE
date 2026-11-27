using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public sealed class HarvestAssignmentService : IHarvestAssignmentService
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;
    private readonly FarmAreaScope _areas;

    private readonly HarvestRecorder _harvestRecorder;

    public HarvestAssignmentService(
        IUnitOfWork uow,
        ICurrentUserService currentUser,
        HarvestRecorder harvestRecorder)
    {
        _uow = uow;
        _currentUser = currentUser;
        _harvestRecorder = harvestRecorder;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    //--------------------------------
    // Manager tạo task
    //--------------------------------
    public async Task<ApiResponse<HarvestAssignmentDto>> CreateAssignmentAsync(
    CreateHarvestAssignmentRequest request, CancellationToken ct = default)
    {
        if (request.FarmingAreaId == Guid.Empty)
            throw AppException.BadRequest("Cần chọn khu nuôi.");

        if (request.AssignedToUserId == Guid.Empty)
            throw AppException.BadRequest("Cần chọn nhân viên.");

        if (request.CrabIds is null || request.CrabIds.Count == 0)
            throw AppException.BadRequest("Assignment phải có ít nhất một con cua.");

        var crabIds = request.CrabIds.Distinct().ToList();
        if (crabIds.Count != request.CrabIds.Count)
            throw AppException.BadRequest("Danh sách cua có mã bị trùng.");

        var manager = await _uow.Users.GetByIdAsync(_currentUser.UserId, ct)
            ?? throw AppException.Unauthorized("Không xác định được Manager.");

        if (!manager.IsActive || manager.Role != UserRole.FarmManager)
            throw AppException.Forbidden("Chỉ Farm Manager đang hoạt động mới được giao task.");

        // Xác nhận Manager được phân công vào khu này.
        await _areas.EnsureAsync(
            request.FarmingAreaId,
            manage: false,
            ct);

        var staff = await _uow.Users.GetByIdAsync(
            request.AssignedToUserId,
            ct);

        if (staff is null || !staff.IsActive || staff.Role != UserRole.FarmStaff)
        {
            throw AppException.BadRequest("Nhân viên không tồn tại, không hoạt động hoặc không phải Farm Staff.");
        }

        // Staff phải thuộc đúng Manager và khu được chọn.
        var staffAssignment =
            await _uow.FarmStaffAssignments.FirstOrDefaultAsync(
                x => x.StaffUserId == staff.Id
                    && x.ManagerUserId == manager.Id
                    && x.FarmingAreaId == request.FarmingAreaId, ct);

        if (staffAssignment is null)
            throw AppException.Forbidden("Nhân viên không thuộc Manager và khu được chọn.");

        var assignment = new HarvestAssignment
        {
            // Mã ngắn hơn giới hạn 40 ký tự trong mapping.
            AssignmentCode = $"HA-{Guid.NewGuid():N}".ToUpperInvariant(),
            FarmingAreaId = request.FarmingAreaId,
            AssignedByUserId = manager.Id,
            AssignedToUserId = staff.Id,
            Status = HarvestAssignmentStatus.Assigned,
            Notes = NormalizeNullable(request.Notes)
        };

        foreach (var crabId in crabIds)
        {
            var crab = await _uow.Crabs.GetByIdAsync(crabId, ct)
                ?? throw AppException.NotFound("Crab");

            // Luồng hiện tại giao task cho cua còn sống.
            if (crab.Status != CrabStatus.Alive)
            {
                throw AppException.Conflict(
                    $"Chỉ cua có trạng thái Alive mới được gán vào Assignment. " +
                    $"Cua {crab.Code} hiện có trạng thái {crab.Status}.");
            }

            if (crab.BoxId is not Guid boxId)
            {
                throw AppException.Conflict($"Cua {crab.Code} chưa thuộc Box nào.");
            }

            var box = await _uow.Boxes.GetByIdAsync(boxId, ct)
                ?? throw AppException.Conflict($"Không tìm thấy Box của cua {crab.Code}.");

            var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);

            if (row is null)
            {
                throw AppException.Conflict($"Không tìm thấy dãy nuôi của cua {crab.Code}.");
            }

            if (row.FarmingAreaId != request.FarmingAreaId)
            {
                throw AppException.BadRequest($"Cua {crab.Code} không thuộc khu được giao.");
            }

            // Không giao cua đang nằm trong một Assignment chưa xử lý xong.
            var hasOpenAssignment =
                await _uow.HarvestAssignmentLines.AnyAsync(
                    line => line.CrabId == crab.Id
                        && (line.Status == HarvestAssignmentLineStatus.Assigned
                            || line.Status == HarvestAssignmentLineStatus.Eligible
                            || line.Status == HarvestAssignmentLineStatus.InProgress), ct);

            if (hasOpenAssignment)
            {
                throw AppException.Conflict($"Cua {crab.Code} đang nằm trong một task chưa hoàn tất.");
            }

            await _harvestRecorder.ValidateCrabsAsync([crab.Id], request.FarmingAreaId,
                checkAlreadyHarvested: true, currentVoucherId: null, ct: ct);

            // Lưu snapshot vị trí và mã cua tại thời điểm Manager giao việc.
            assignment.Lines.Add(new HarvestAssignmentLine
            {
                HarvestAssignmentId = assignment.Id,
                CrabId = crab.Id,
                CrabCode = crab.Code,
                BoxId = box.Id,
                BoxCode = box.Code,
                Status = HarvestAssignmentLineStatus.Assigned
            });
        }

        await _uow.HarvestAssignments.AddAsync(assignment, ct);
        await _uow.SaveChangesAsync(ct);

        var response = new HarvestAssignmentDto(
            assignment.Id,
            assignment.AssignmentCode,
            assignment.FarmingAreaId,
            assignment.AssignedToUserId,
            assignment.Status.ToString(),
            assignment.Lines.Count);

        return ApiResponse<HarvestAssignmentDto>.Ok(response, "Đã tạo nhiệm vụ thu hoạch.");
    }
    //--------------------------------
    // Staff lưu kết quả xử lý từng cua.
    // Voucher chỉ được tạo trong CompleteAssignmentAsync.
    //--------------------------------
    public async Task<ApiResponse<HarvestAssignmentResultDto>>
        SubmitAssignmentResultsAsync(
            Guid assignmentId,
            SubmitHarvestAssignmentResultsRequest request,
            CancellationToken ct = default)
    {
        var assignment = await _uow.HarvestAssignments.GetByIdAsync(
            assignmentId,
            ct);

        if (assignment is null)
            throw AppException.NotFound("HarvestAssignment");

        // Chỉ Staff được giao task mới được ghi kết quả.
        if (assignment.AssignedToUserId != _currentUser.UserId)
            throw AppException.Forbidden("Bạn không được giao nhiệm vụ này.");

        await _areas.EnsureAsync(
            assignment.FarmingAreaId,
            manage: false,
            ct);

        // Staff phải nhận task trước khi ghi kết quả.
        if (assignment.Status != HarvestAssignmentStatus.InProgress)
            throw AppException.Conflict("Cần nhận Assignment trước khi xử lý.");

        var submitted = request.Lines?.ToList() ?? [];

        if (submitted.Count == 0)
            throw AppException.BadRequest("Cần gửi ít nhất một kết quả.");

        if (submitted.Select(x => x.AssignmentLineId).Distinct().Count()
            != submitted.Count)
        {
            throw AppException.BadRequest("Không được gửi trùng Assignment Line.");
        }

        var assignmentLines =
            (await _uow.HarvestAssignmentLines.FindAsync(
                line => line.HarvestAssignmentId == assignmentId,
                ct))
            .ToList();

        if (assignmentLines.Count == 0)
            throw AppException.Conflict("Assignment không có cua được giao.");

        var linesById = assignmentLines.ToDictionary(line => line.Id);

        var now = DateTime.UtcNow;


        // Kiểm tra toàn bộ request trước khi sửa entity.
        foreach (var result in submitted)
        {
            if (!Enum.IsDefined(
                    typeof(HarvestAssignmentOutcome),
                    result.Outcome))
            {
                throw AppException.BadRequest("Outcome không hợp lệ.");
            }

            if (!linesById.TryGetValue(
                    result.AssignmentLineId,
                    out var assignmentLine))
            {
                throw AppException.BadRequest("Có Assignment Line không thuộc nhiệm vụ này.");
            }

            // Chỉ nhận kết quả cho các dòng thuộc task Staff đã nhận.
            // Cho phép cập nhật lại kết quả khi task vẫn đang InProgress.
            if (assignmentLine.Status != HarvestAssignmentLineStatus.InProgress)
            {
                throw AppException.Conflict("Dòng cua không thuộc Assignment đang được xử lý.");
            }

            if (result.Outcome == HarvestAssignmentOutcome.ContinueRaising
                && string.IsNullOrWhiteSpace(result.Reason))
            {
                throw AppException.BadRequest("Cần ghi lý do chọn nuôi tiếp.");
            }

            if (result.Outcome == HarvestAssignmentOutcome.Harvested
                && (!result.WeightGram.HasValue || result.WeightGram.Value <= 0))
            {
                throw AppException.BadRequest("Cua thu hoạch phải có khối lượng lớn hơn 0.");
            }

            if (result.Outcome == HarvestAssignmentOutcome.Harvested)
            {
                if (!result.CarapaceLengthMm.HasValue || result.CarapaceLengthMm.Value <= 0)
                    throw AppException.BadRequest("Chiều dài mai sau lột phải lớn hơn 0.");

                if (!result.CarapaceWidthMm.HasValue || result.CarapaceWidthMm.Value <= 0)
                    throw AppException.BadRequest("Chiều rộng mai sau lột phải lớn hơn 0.");

                if (!result.HarvestedAt.HasValue)
                    throw AppException.BadRequest("Cần nhập ngày giờ thu hoạch.");
            }

            var crab = await _uow.Crabs.GetByIdAsync(assignmentLine.CrabId, ct)
    ?? throw AppException.Conflict($"Không tìm thấy cua {assignmentLine.CrabCode}.");

            EnsureAssignmentLocation(assignmentLine, crab);
            if (result.Outcome == HarvestAssignmentOutcome.Harvested)
                EnsureHarvestTime(crab, result.HarvestedAt!.Value.UtcDateTime, now);

            var hasMolted = result.MoltConfirmed || crab.MoltedAt.HasValue;

            if (result.Outcome == HarvestAssignmentOutcome.ContinueRaising
                && !hasMolted
                && now > crab.StockedAt.AddDays(45))
            {
                throw AppException.Conflict(
                    $"Cua {assignmentLine.CrabCode} đã quá 45 ngày từ ngày thả nuôi và chưa lột; bắt buộc thu hoạch.");
            }
        }

        // Xác nhận các cua gửi lên vẫn thuộc khu và chưa bị thu hoạch ở nơi khác.
        var submittedCrabIds = submitted
            .Select(result => linesById[result.AssignmentLineId].CrabId)
            .Distinct()
            .ToList();

        await _harvestRecorder.ValidateCrabsAsync(
            submittedCrabIds,
            assignment.FarmingAreaId,
            checkAlreadyHarvested: true,
            currentVoucherId: null,
            ct: ct, currentAssignmentId: assignment.Id);

        // Chạy các kiểm tra thu hoạch chỉ trên những cua Staff chọn thu hoạch.
        var harvestedResults = submitted
            .Where(result => result.Outcome == HarvestAssignmentOutcome.Harvested)
            .ToList();

        if (harvestedResults.Count > 0)
        {
            var harvestRequests = harvestedResults
                .Select(result =>
                {
                    var assignmentLine = linesById[result.AssignmentLineId];

                    return new HarvestLineRequest(
                        CrabId: assignmentLine.CrabId,
                        WeightGram: result.WeightGram!.Value,
                        Grade: result.Grade,
                        IsSoftshell: result.IsSoftshell,
                        Notes: result.Notes,
                        ConditionLabel: result.ConditionLabel,
                        PhotoUrls: result.PhotoUrls,
                        Result: "passed");
                })
                .ToList();

            _harvestRecorder.ValidateHarvestLines(
                harvestRequests,
                requirePositiveWeight: true);
        }


        // Chỉ lưu lựa chọn và thông tin tạm trên Assignment Line.
        // Giữ Status = InProgress để cua vẫn được giữ trong task đang mở.
        foreach (var result in submitted)
        {
            var line = linesById[result.AssignmentLineId];

            line.Outcome = result.Outcome;
            line.CheckedAt = now;
            line.CheckedByUserId = _currentUser.UserId;
            line.Notes = NormalizeNullable(result.Notes);
            line.PendingMoltConfirmed = result.MoltConfirmed;

            // Chưa thu hoạch thật ở bước này nên chưa ghi HarvestedAt.
            line.HarvestedAt = null;

            if (result.Outcome == HarvestAssignmentOutcome.ContinueRaising)
            {
                line.EligibilityReason = NormalizeNullable(result.Reason);

                // Xóa dữ liệu thu hoạch cũ nếu Staff đổi lựa chọn sang nuôi tiếp.
                line.HarvestWeightGram = null;
                line.HarvestGrade = null;
                line.HarvestCarapaceLengthMm = null;
                line.HarvestCarapaceWidthMm = null;
                line.PendingHarvestedAt = null;
                line.HarvestIsSoftshell = null;
                line.HarvestConditionLabel = null;
                line.HarvestNotes = null;
                line.HarvestPhotoUrlsJson = "[]";
            }
            else
            {
                line.EligibilityReason = null;
                line.HarvestWeightGram = result.WeightGram;
                line.HarvestGrade = NormalizeNullable(result.Grade);
                line.HarvestCarapaceLengthMm = result.CarapaceLengthMm;
                line.HarvestCarapaceWidthMm = result.CarapaceWidthMm;
                line.PendingHarvestedAt = result.HarvestedAt!.Value.UtcDateTime;
                line.HarvestIsSoftshell = result.IsSoftshell;
                line.HarvestConditionLabel =
                    NormalizeNullable(result.ConditionLabel);
                line.HarvestNotes = NormalizeNullable(result.Notes);
                line.HarvestPhotoUrlsJson =
                    JsonStringList.Serialize(result.PhotoUrls);
            }

            _uow.HarvestAssignmentLines.Update(line);
        }

        await _uow.SaveChangesAsync(ct);

        // Trả số lượng kết quả đã lưu trên toàn task, không chỉ request lần này.
        var harvestedCount = assignmentLines.Count(
            line => IsActiveLine(line) && line.Outcome == HarvestAssignmentOutcome.Harvested);

        var deferredCount = assignmentLines.Count(
            line => IsActiveLine(line) && line.Outcome == HarvestAssignmentOutcome.ContinueRaising);

        return ApiResponse<HarvestAssignmentResultDto>.Ok(
            new HarvestAssignmentResultDto(
                assignment.Id,
                assignment.AssignmentCode,
                assignment.Status.ToString(),
                VoucherId: null,
                VoucherCode: null,
                HarvestedCount: harvestedCount,
                DeferredCount: deferredCount),
            "Đã lưu kết quả xử lý cua.");
    }

    //--------------------------------
    // Manager xem task trong khu của mình; Staff chỉ xem task được giao cho mình
    //--------------------------------
    public async Task<ApiResponse<IEnumerable<HarvestAssignmentListItemDto>>>
    GetAssignmentsAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        var caller = await _areas.CurrentUserAsync(ct)
            ?? throw AppException.Unauthorized();

        if (caller.Role is not UserRole.FarmManager
            and not UserRole.FarmStaff)
        {
            throw AppException.Forbidden("Chỉ Farm Manager hoặc Farm Staff được xem Assignment.");
        }

        // Lấy danh sách khu từ phân công trong database, không tin khu do client gửi.
        var allowedAreaIds = await _areas.AreaIdsAsync(ct)
            ?? new HashSet<Guid>();

        if (farmingAreaId.HasValue)
        {
            await _areas.EnsureAsync(
                farmingAreaId.Value,
                manage: false,
                ct);

            allowedAreaIds.IntersectWith(new[] { farmingAreaId.Value });
        }

        if (allowedAreaIds.Count == 0)
        {
            return ApiResponse<IEnumerable<HarvestAssignmentListItemDto>>.Ok(
                Array.Empty<HarvestAssignmentListItemDto>());
        }

        // Manager xem task trong khu mình quản lý.
        // Staff chỉ xem task được giao cho chính mình.
        var assignments = caller.Role == UserRole.FarmManager
            ? (await _uow.HarvestAssignments.FindAsync(
                    x => allowedAreaIds.Contains(x.FarmingAreaId),
                    ct))
                .ToList()
            : (await _uow.HarvestAssignments.FindAsync(
                    x => x.AssignedToUserId == caller.Id
                        && allowedAreaIds.Contains(x.FarmingAreaId),
                    ct))
                .ToList();

        assignments = assignments
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        if (assignments.Count == 0)
        {
            return ApiResponse<IEnumerable<HarvestAssignmentListItemDto>>.Ok(
                Array.Empty<HarvestAssignmentListItemDto>());
        }

        var assignmentIds = assignments
            .Select(x => x.Id)
            .ToList();

        var lines = (await _uow.HarvestAssignmentLines.FindAsync(
            x => assignmentIds.Contains(x.HarvestAssignmentId),
            ct)).ToList();

        var vouchers = (await _uow.HarvestVouchers.FindAsync(
            x => x.HarvestAssignmentId.HasValue
                && assignmentIds.Contains(x.HarvestAssignmentId.Value),
            ct)).ToList();

        var linesByAssignment = lines
            .GroupBy(x => x.HarvestAssignmentId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var vouchersByAssignment = vouchers
            .Where(x => x.HarvestAssignmentId.HasValue)
            .GroupBy(x => x.HarvestAssignmentId!.Value)
            .ToDictionary(x => x.Key, x => x.Count());

        var result = assignments.Select(assignment =>
        {
            var assignmentLines = linesByAssignment.TryGetValue(
                assignment.Id,
                out var foundLines)
                    ? foundLines
                    : new List<HarvestAssignmentLine>();

            return new HarvestAssignmentListItemDto(
                assignment.Id,
                assignment.AssignmentCode,
                assignment.FarmingAreaId,
                assignment.AssignedToUserId,
                assignment.Status.ToString(),
                assignment.CreatedAt,
                assignment.StartedAt,
                assignment.CompletedAt,
                assignmentLines.Count,
                assignmentLines.Count(
                    x => IsActiveLine(x) && x.Outcome == HarvestAssignmentOutcome.Harvested
                        || x.Status == HarvestAssignmentLineStatus.Harvested),

                assignmentLines.Count(
                    x => IsActiveLine(x) && x.Outcome == HarvestAssignmentOutcome.ContinueRaising
                        || x.Status == HarvestAssignmentLineStatus.Deferred),
                vouchersByAssignment.GetValueOrDefault(assignment.Id),
                assignmentLines.Count(x => x.Status == HarvestAssignmentLineStatus.Skipped));
        }).ToList();

        return ApiResponse<IEnumerable<HarvestAssignmentListItemDto>>.Ok(result);
    }

    //--------------------------------
    // Manager xem task trong khu của mình; Staff chỉ xem task được giao cho mình (ID)
    //--------------------------------
    public async Task<ApiResponse<HarvestAssignmentDetailDto>>
        GetAssignmentByIdAsync(
            Guid assignmentId,
            CancellationToken ct = default)
    {
        var assignment = await _uow.HarvestAssignments.GetByIdAsync(
            assignmentId,
            ct);

        if (assignment is null)
            throw AppException.NotFound("HarvestAssignment");

        var caller = await _areas.CurrentUserAsync(ct)
            ?? throw AppException.Unauthorized();

        // Manager chỉ xem Assignment thuộc khu được phân công.
        if (caller.Role == UserRole.FarmManager)
        {
            await _areas.EnsureAsync(
                assignment.FarmingAreaId,
                manage: false,
                ct);
        }
        // Staff chỉ xem Assignment của mình.
        else if (caller.Role == UserRole.FarmStaff)
        {
            if (assignment.AssignedToUserId != caller.Id)
                throw AppException.Forbidden(
                    "Assignment này không được giao cho bạn.");

            await _areas.EnsureAsync(
                assignment.FarmingAreaId,
                manage: false,
                ct);
        }
        else
        {
            throw AppException.Forbidden(
                "Chỉ Farm Manager hoặc Farm Staff được xem Assignment.");
        }

        var lines = (await _uow.HarvestAssignmentLines.FindAsync(
            x => x.HarvestAssignmentId == assignment.Id,
            ct)).ToList();

        var lineIds = lines.Select(x => x.Id).ToList();

        // Chỉ lấy HarvestLine liên quan tới các dòng của Assignment này.
        var harvestLines = lineIds.Count == 0
            ? new List<HarvestLine>()
            : (await _uow.HarvestLines.FindAsync(
                x => x.HarvestAssignmentLineId.HasValue
                    && lineIds.Contains(x.HarvestAssignmentLineId.Value),
                ct)).ToList();

        var harvestLinesByAssignmentLine = harvestLines
            .Where(x => x.HarvestAssignmentLineId.HasValue)
            .ToDictionary(
                x => x.HarvestAssignmentLineId!.Value,
                x => x);

        var vouchers = (await _uow.HarvestVouchers.FindAsync(
            x => x.HarvestAssignmentId == assignment.Id,
            ct)).ToList();

        var lineDtos = lines.Select(line =>
        {
            harvestLinesByAssignmentLine.TryGetValue(
                line.Id,
                out var harvestLine);

            return new HarvestAssignmentLineDetailDto(
                line.Id,
                line.CrabId,
                line.CrabCode,
                line.BoxId,
                line.BoxCode,
                line.Status.ToString(),
                line.CheckedAt,
                line.CheckedByUserId,
                line.EligibilityReason,
                line.HarvestedAt,
                line.Notes,
                harvestLine?.Id,
                harvestLine?.WeightGram,
                harvestLine?.Grade,
                line.Outcome,
                line.HarvestWeightGram,
                line.HarvestGrade,
                line.HarvestIsSoftshell,
                line.HarvestConditionLabel,
                line.HarvestNotes,
                line.HarvestCarapaceLengthMm,
                line.HarvestCarapaceWidthMm,
                line.PendingHarvestedAt,
                JsonStringList.Parse(line.HarvestPhotoUrlsJson),
                line.PendingMoltConfirmed);
        }).ToList();

        var voucherDtos = vouchers
            .OrderBy(x => x.HarvestDate)
            .Select(voucher => new HarvestAssignmentVoucherDto(
                voucher.Id,
                voucher.VoucherCode,
                voucher.Status.ToString(),
                voucher.HarvestDate,
                voucher.TotalQuantity,
                voucher.TotalWeightKg))
            .ToList();

        var response = new HarvestAssignmentDetailDto(
            assignment.Id,
            assignment.AssignmentCode,
            assignment.FarmingAreaId,
            assignment.AssignedByUserId,
            assignment.AssignedToUserId,
            assignment.Status.ToString(),
            assignment.CreatedAt,
            assignment.StartedAt,
            assignment.CompletedAt,
            assignment.Notes,
            lineDtos,
            voucherDtos,
            assignment.SubmittedAt, assignment.SubmissionNotes, assignment.ReviewedAt,
            assignment.ReviewedByUserId, assignment.ReviewNotes, ReadAudit(assignment));

        return ApiResponse<HarvestAssignmentDetailDto>.Ok(response);
    }

    //--------------------------------
    // Staff nhận task được giao
    //--------------------------------
    public async Task<ApiResponse<HarvestAssignmentDto>> AcceptAssignmentAsync(
    Guid assignmentId,
    CancellationToken ct = default)
    {
        var assignment = await _uow.HarvestAssignments.GetByIdAsync(assignmentId, ct)
            ?? throw AppException.NotFound("HarvestAssignment");

        if (assignment.AssignedToUserId != _currentUser.UserId)
            throw AppException.Forbidden("Bạn không được giao nhiệm vụ này.");

        await _areas.EnsureAsync(assignment.FarmingAreaId, manage: false, ct);

        if (assignment.Status != HarvestAssignmentStatus.Assigned)
            throw AppException.Conflict("Assignment không ở trạng thái chờ nhận.");

        var now = DateTime.UtcNow;
        assignment.Status = HarvestAssignmentStatus.InProgress;
        assignment.StartedAt = now;

        var lines = (await _uow.HarvestAssignmentLines.FindAsync(
            x => x.HarvestAssignmentId == assignmentId,
            ct)).ToList();

        if (lines.Count == 0)
            throw AppException.Conflict("Assignment không có cua được giao.");

        // Giữ tất cả cua trong task đang mở cho đến khi Staff nộp task.
        foreach (var line in lines.Where(IsActiveLine))
        {
            line.Status = HarvestAssignmentLineStatus.InProgress;
            _uow.HarvestAssignmentLines.Update(line);
        }

        _uow.HarvestAssignments.Update(assignment);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<HarvestAssignmentDto>.Ok(
            new HarvestAssignmentDto(
                assignment.Id,
                assignment.AssignmentCode,
                assignment.FarmingAreaId,
                assignment.AssignedToUserId,
                assignment.Status.ToString(),
                lines.Count),
            "Đã nhận nhiệm vụ thu hoạch.");
    }

    //--------------------------------
    // Staff hoàn tất task được giao
    //--------------------------------
    public async Task<ApiResponse<HarvestAssignmentResultDto>> CompleteAssignmentAsync(
        Guid assignmentId,
        CompleteHarvestAssignmentRequest request,
        CancellationToken ct = default)
    {
        var assignment = await _uow.HarvestAssignments.GetByIdAsync(assignmentId, ct)
            ?? throw AppException.NotFound("HarvestAssignment");
        var caller = await _areas.CurrentUserAsync(ct) ?? throw AppException.Unauthorized();
        if (caller.Role != UserRole.FarmStaff || assignment.AssignedToUserId != caller.Id)
            throw AppException.Forbidden("Bạn không được giao nhiệm vụ này.");
        await _areas.EnsureAsync(assignment.FarmingAreaId, manage: false, ct);

        if (assignment.Status is not (HarvestAssignmentStatus.InProgress or HarvestAssignmentStatus.PendingReview))
            throw AppException.Conflict("Chỉ có thể hoàn tất nhiệm vụ đang xử lý.");

        var now = DateTime.UtcNow;
        var (lines, harvestedLines, deferredLines) =
            await ValidateCompletionAsync(assignment, now, ct);
        //Gán cua vào lot cấp đông nếu có cua được thu hoạch
        FrozenLot? openLot = null;
        var createdCrabItems = new List<FrozenCrabItem>();

        if (harvestedLines.Count > 0)
        {
            // Chỉ yêu cầu lot khi Assignment có cua được thu hoạch.
            var openLots = (await _uow.FrozenLots.FindAsync(
                x => x.FarmingAreaId == assignment.FarmingAreaId
                     && x.IsOpenForInbound,
                ct)).ToList();

            if (openLots.Count == 0)
                throw AppException.Conflict(
                    "Khu chưa có Frozen Lot đang mở. Vui lòng liên hệ Manager tạo lot.");

            if (openLots.Count != 1)
                throw AppException.Conflict(
                    "Dữ liệu Frozen Lot không hợp lệ: khu phải có đúng một lot đang mở.");

            openLot = openLots[0];

            // Chỉ cho dùng các grade đã thống nhất với Harvest Line.
            var allowedCrabGrades = new HashSet<string>(
                ["S", "M", "L", "XL", "XXL", "A", "B", "C"],
                StringComparer.OrdinalIgnoreCase);

            if (harvestedLines.Any(line =>
                    string.IsNullOrWhiteSpace(line.HarvestGrade)
                    || !allowedCrabGrades.Contains(line.HarvestGrade.Trim())))
                throw AppException.BadRequest(
                    "Grade của cua thu hoạch không hợp lệ.");
        }
        assignment.SubmittedAt ??= now;
        assignment.SubmissionNotes = NormalizeNullable(request.VoucherNotes);
        AppendAudit(assignment, "completed", assignment.SubmissionNotes);

        HarvestVoucher? voucher = null;
        var createdHarvestLines = new List<HarvestLine>();
        if (harvestedLines.Count > 0)
        {
            var staff = await _uow.Users.GetByIdAsync(assignment.AssignedToUserId, ct);
            voucher = new HarvestVoucher
            {
                VoucherCode = await _harvestRecorder.GenerateVoucherCodeAsync(ct),
                HarvestAssignmentId = assignment.Id,
                HarvestDate = harvestedLines.Max(line => line.PendingHarvestedAt!.Value),
                Status = HarvestStatus.Completed,
                CreatedBy = assignment.AssignedToUserId,
                PerformedByUserId = assignment.AssignedToUserId,
                PerformedByName = staff?.FullName,
                FarmingAreaId = assignment.FarmingAreaId,
                ExecutionCompletedAt = now,
                Notes = assignment.SubmissionNotes
            };

            foreach (var line in harvestedLines)
            {
                var lineRequest = new HarvestLineRequest(
                    CrabId: line.CrabId,
                    WeightGram: line.HarvestWeightGram!.Value,
                    Grade: line.HarvestGrade,
                    IsSoftshell: line.HarvestIsSoftshell ?? true,
                    Notes: line.HarvestNotes,
                    ConditionLabel: line.HarvestConditionLabel,
                    PhotoUrls: JsonStringList.Parse(line.HarvestPhotoUrlsJson),
                    Result: "passed");

                var harvestLine = await _harvestRecorder.BuildLineAsync(
                    voucher.Id,
                    lineRequest,
                    ct);

                harvestLine.HarvestAssignmentLineId = line.Id;
                harvestLine.AssignmentLine = line;
                line.HarvestLine = harvestLine;
                harvestLine.CarapaceLengthMm = line.HarvestCarapaceLengthMm;
                harvestLine.CarapaceWidthMm = line.HarvestCarapaceWidthMm;
                harvestLine.HarvestedAt = line.PendingHarvestedAt;

                line.Status = HarvestAssignmentLineStatus.Harvested;
                line.HarvestedAt = line.PendingHarvestedAt;
                line.EligibilityReason = null;
                voucher.Lines.Add(harvestLine);
                createdHarvestLines.Add(harvestLine);
            }

            voucher.TotalQuantity = createdHarvestLines.Count;
            voucher.TotalWeightKg = CalculateTotalWeightKg(createdHarvestLines);
            await _uow.HarvestVouchers.AddAsync(voucher, ct);

            foreach (var line in harvestedLines)
                await ApplyMoltConfirmationAsync(assignment, line, now, ct);

            await _harvestRecorder.ApplyHarvestAsync(
                voucher,
                createdHarvestLines,
                ct);
        }
        //Tạo barcode
        if (openLot is not null && voucher is not null)
        {
            foreach (var harvestLine in createdHarvestLines)
            {
                var assignmentLine = harvestedLines.Single(
                    line => line.Id == harvestLine.HarvestAssignmentLineId);

                var itemGrade = assignmentLine.HarvestGrade?.Trim();

                if (string.IsNullOrWhiteSpace(itemGrade))
                    throw AppException.BadRequest(
                        "Cua thu hoạch chưa có grade hợp lệ.");

                var crab = await _uow.Crabs.GetByIdAsync(
                    assignmentLine.CrabId, ct)
                    ?? throw AppException.Conflict(
                        "Không tìm thấy cua của Harvest Line.");

                var item = new FrozenCrabItem
                {
                    FrozenLotId = openLot.Id,
                    HarvestLineId = harvestLine.Id,
                    CrabId = crab.Id,
                    BarcodeValue = $"FC-{Guid.NewGuid():N}".ToUpperInvariant(),
                    LotCode = openLot.LotCode,
                    CrabCode = crab.Code,
                    HarvestVoucherCode = voucher.VoucherCode,
                    FrozenDate = openLot.FrozenDate,
                    ExpiryDate = openLot.ExpiryDate,
                    WeightGram = harvestLine.WeightGram,
                    Grade = itemGrade.ToUpperInvariant(),
                    // Chưa tính vào tồn kho cho đến khi phiếu nhập được duyệt và thực thi.
                    StockState = "PendingInbound"
                };

                await _uow.FrozenCrabItems.AddAsync(item, ct);
                createdCrabItems.Add(item);
            }

            // Token giúp phát hiện hai Assignment cập nhật cùng lot đồng thời.
            openLot.ConcurrencyVersion++;
            _uow.FrozenLots.Update(openLot);
        }

        foreach (var line in deferredLines)
        {
            await ApplyMoltConfirmationAsync(assignment, line, now, ct);
            line.Status = HarvestAssignmentLineStatus.Deferred;
        }

        assignment.Status = HarvestAssignmentStatus.Completed;
        assignment.CompletedAt = now;
        foreach (var line in lines)
            _uow.HarvestAssignmentLines.Update(line);

        _uow.HarvestAssignments.Update(assignment);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<HarvestAssignmentResultDto>.Ok(
            new HarvestAssignmentResultDto(
                assignment.Id,
                assignment.AssignmentCode,
                assignment.Status.ToString(),
                voucher?.Id,
                voucher?.VoucherCode,
                harvestedLines.Count,
                deferredLines.Count,
                openLot?.Id,
createdCrabItems
    .Select(item => new HarvestCrabLabelDto(
        item.Id,
        item.CrabId,
        item.CrabCode,
        item.BarcodeValue))
    .ToList()),
            "Đã hoàn tất nhiệm vụ thu hoạch.");
    }

    private static bool IsActiveLine(HarvestAssignmentLine line)
        => line.Status is HarvestAssignmentLineStatus.Assigned or HarvestAssignmentLineStatus.Eligible or HarvestAssignmentLineStatus.InProgress;

    private static List<HarvestAssignmentAuditDto> ReadAudit(HarvestAssignment assignment)
        => string.IsNullOrWhiteSpace(assignment.WorkflowAuditJson)
            ? []
            : JsonSerializer.Deserialize<List<HarvestAssignmentAuditDto>>(assignment.WorkflowAuditJson) ?? [];

    private void AppendAudit(HarvestAssignment assignment, string action, string? reason = null,
        Guid? lineId = null, Guid? previousStaffId = null, Guid? newStaffId = null)
    {
        var history = ReadAudit(assignment);
        history.Add(new HarvestAssignmentAuditDto(DateTime.UtcNow, _currentUser.UserId, action, reason, lineId, previousStaffId, newStaffId));
        assignment.WorkflowAuditJson = JsonSerializer.Serialize(history);
    }

    private static string RequireReason(string? value)
    {
        var reason = NormalizeNullable(value);
        if (reason is null || reason.Length > 1000)
            throw AppException.BadRequest("Cần lý do xử lý (tối đa 1000 ký tự).");
        return reason;
    }

    private async Task<HarvestAssignment> GetOpenManagerAssignmentAsync(Guid id, CancellationToken ct)
    {
        var caller = await _areas.CurrentUserAsync(ct) ?? throw AppException.Unauthorized();
        if (caller.Role != UserRole.FarmManager)
            throw AppException.Forbidden("Chỉ Farm Manager được xử lý nhiệm vụ.");
        var assignment = await _uow.HarvestAssignments.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("HarvestAssignment");
        await _areas.EnsureAsync(assignment.FarmingAreaId, manage: false, ct);
        if (assignment.Status is not (HarvestAssignmentStatus.Assigned or HarvestAssignmentStatus.InProgress or HarvestAssignmentStatus.PendingReview))
            throw AppException.Conflict("Nhiệm vụ đã kết thúc.");
        return assignment;
    }

    private async Task<List<HarvestAssignmentLine>> GetUnworkedLinesAsync(HarvestAssignment assignment, CancellationToken ct)
    {
        if (assignment.Status == HarvestAssignmentStatus.PendingReview || assignment.SubmittedAt.HasValue)
            throw AppException.Conflict("Nhiệm vụ đã gửi kết quả; không thể hủy hoặc giao lại.");
        var lines = (await _uow.HarvestAssignmentLines.FindAsync(x => x.HarvestAssignmentId == assignment.Id, ct)).ToList();
        if (lines.Count == 0 || lines.Any(x => x.Outcome.HasValue || x.CheckedAt.HasValue || x.PendingMoltConfirmed || x.PendingHarvestedAt.HasValue))
            throw AppException.Conflict("Nhiệm vụ đã có kết quả; cần đối soát thay vì hủy hoặc giao lại.");
        return lines.Where(IsActiveLine).ToList();
    }

    public async Task<ApiResponse<HarvestAssignmentResultDto>> CancelAssignmentAsync(
        Guid assignmentId, ReviewHarvestAssignmentRequest request, CancellationToken ct = default)
    {
        var assignment = await GetOpenManagerAssignmentAsync(assignmentId, ct);
        var reason = RequireReason(request.Notes);
        var lines = await GetUnworkedLinesAsync(assignment, ct);
        foreach (var line in lines)
        {
            line.Status = HarvestAssignmentLineStatus.Cancelled;
            _uow.HarvestAssignmentLines.Update(line);
        }
        assignment.Status = HarvestAssignmentStatus.Cancelled;
        assignment.CompletedAt = DateTime.UtcNow;
        AppendAudit(assignment, "cancelled", reason);
        _uow.HarvestAssignments.Update(assignment);
        await _uow.SaveChangesAsync(ct);
        return OperationResult(assignment, "Đã hủy nhiệm vụ, không thay đổi cua hoặc hộp.");
    }

    public async Task<ApiResponse<HarvestAssignmentResultDto>> ReassignAssignmentAsync(
        Guid assignmentId, ReassignHarvestAssignmentRequest request, CancellationToken ct = default)
    {
        var assignment = await GetOpenManagerAssignmentAsync(assignmentId, ct);
        var reason = RequireReason(request.Reason);
        var lines = await GetUnworkedLinesAsync(assignment, ct);
        if (lines.Count == 0)
            throw AppException.Conflict("Không còn cua cần giao.");
        var staff = await _uow.Users.GetByIdAsync(request.AssignedToUserId, ct);
        if (staff is null || !staff.IsActive || staff.Role != UserRole.FarmStaff || staff.Id == assignment.AssignedToUserId)
            throw AppException.BadRequest("Cần chọn Farm Staff khác đang hoạt động.");
        var membership = await _uow.FarmStaffAssignments.FirstOrDefaultAsync(x =>
            x.StaffUserId == staff.Id && x.ManagerUserId == _currentUser.UserId && x.FarmingAreaId == assignment.FarmingAreaId, ct);
        if (membership is null)
            throw AppException.Forbidden("Nhân viên không thuộc Manager và khu được chọn.");
        var previous = assignment.AssignedToUserId;
        foreach (var line in lines)
        {
            var crab = await _uow.Crabs.GetByIdAsync(line.CrabId, ct)
                ?? throw AppException.NotFound("Crab");
            EnsureAssignmentLocation(line, crab);
        }
        await _harvestRecorder.ValidateCrabsAsync(lines.Select(x => x.CrabId).ToList(), assignment.FarmingAreaId,
            checkAlreadyHarvested: true, currentVoucherId: null, ct: ct, currentAssignmentId: assignment.Id);
        foreach (var line in lines)
        {
            line.Status = HarvestAssignmentLineStatus.Assigned;
            _uow.HarvestAssignmentLines.Update(line);
        }
        assignment.AssignedToUserId = staff.Id;
        assignment.Status = HarvestAssignmentStatus.Assigned;
        assignment.StartedAt = null;
        AppendAudit(assignment, "reassigned", reason, previousStaffId: previous, newStaffId: staff.Id);
        _uow.HarvestAssignments.Update(assignment);
        await _uow.SaveChangesAsync(ct);
        return OperationResult(assignment, "Đã giao lại nhiệm vụ.");
    }

    public async Task<ApiResponse<HarvestAssignmentResultDto>> SkipAssignmentLineAsync(
        Guid assignmentId, Guid lineId, SkipHarvestAssignmentLineRequest request, CancellationToken ct = default)
    {
        var assignment = await GetOpenManagerAssignmentAsync(assignmentId, ct);
        var reason = RequireReason(request.Reason);
        var line = await _uow.HarvestAssignmentLines.GetByIdAsync(lineId, ct)
            ?? throw AppException.NotFound("HarvestAssignmentLine");
        if (line.HarvestAssignmentId != assignment.Id || !IsActiveLine(line))
            throw AppException.Conflict("Dòng cua không thuộc nhiệm vụ đang mở.");
        var crab = await _uow.Crabs.GetByIdAsync(line.CrabId, ct)
            ?? throw AppException.NotFound("Crab");
        var terminal = crab.Status is CrabStatus.Dead or CrabStatus.Missing;
        var moved = crab.BoxId.HasValue && crab.BoxId != line.BoxId;
        if (!terminal && !moved)
            throw AppException.Conflict("Chỉ xử lý ngoại lệ cua đã chết, thất lạc hoặc đổi vị trí.");
        if (!terminal && (line.Outcome == HarvestAssignmentOutcome.Harvested || line.PendingMoltConfirmed))
            throw AppException.Conflict("Cua có bằng chứng thu hoạch/lột; cần đối soát trước.");
        // Retain submitted evidence verbatim, but never count skipped results as harvest.
        // No crab/box mutation here; mortality/missing/transfer owns those changes.
        line.Status = HarvestAssignmentLineStatus.Skipped;
        AppendAudit(assignment, "line-skipped", $"{crab.Status}; {reason}", line.Id);
        _uow.HarvestAssignmentLines.Update(line);
        _uow.HarvestAssignments.Update(assignment);
        await _uow.SaveChangesAsync(ct);
        return OperationResult(assignment, "Đã xử lý ngoại lệ, giữ nguyên hồ sơ cua và kết quả đã gửi.");
    }

    private static ApiResponse<HarvestAssignmentResultDto> OperationResult(HarvestAssignment assignment, string message)
        => ApiResponse<HarvestAssignmentResultDto>.Ok(new HarvestAssignmentResultDto(
            assignment.Id, assignment.AssignmentCode, assignment.Status.ToString(), null, null, 0, 0), message);

    private async Task<(List<HarvestAssignmentLine> Lines, List<HarvestAssignmentLine> Harvested, List<HarvestAssignmentLine> Deferred)>
        ValidateCompletionAsync(HarvestAssignment assignment, DateTime now, CancellationToken ct)
    {
        var lines = (await _uow.HarvestAssignmentLines.FindAsync(
            x => x.HarvestAssignmentId == assignment.Id,
            ct)).ToList();

        if (lines.Count == 0)
            throw AppException.Conflict("Assignment không có cua được giao.");

        // Skipped lines retain their evidence, but are not candidates for voucher creation.
        lines = lines.Where(IsActiveLine).ToList();
        if (lines.Any(x => x.Outcome is null))
            throw AppException.Conflict("Cần xử lý tất cả cua trước khi hoàn tất task.");

        var harvestedLines = lines
            .Where(x => x.Outcome == HarvestAssignmentOutcome.Harvested)
            .ToList();

        var deferredLines = lines
            .Where(x => x.Outcome == HarvestAssignmentOutcome.ContinueRaising)
            .ToList();

        if (deferredLines.Any(x => string.IsNullOrWhiteSpace(x.EligibilityReason)))
            throw AppException.Conflict("Cần có lý do nuôi tiếp cho tất cả cua được hoãn.");

        // Revalidate ALL pending outcomes, including deferred crabs, before
        // creating a voucher or applying any molt/harvest effects.
        await _harvestRecorder.ValidateCrabsAsync(
            lines.Select(line => line.CrabId).Distinct().ToList(),
            assignment.FarmingAreaId, checkAlreadyHarvested: true,
            currentVoucherId: null, ct: ct, currentAssignmentId: assignment.Id);
        foreach (var line in lines)
        {
            var crab = await _uow.Crabs.GetByIdAsync(line.CrabId, ct)
                ?? throw AppException.Conflict($"Không tìm thấy cua {line.CrabCode}.");
            EnsureAssignmentLocation(line, crab);
            if (line.Outcome == HarvestAssignmentOutcome.Harvested && line.PendingHarvestedAt is DateTime at)
                EnsureHarvestTime(crab, at, now);
        }

        foreach (var line in deferredLines)
        {
            var crab = await _uow.Crabs.GetByIdAsync(line.CrabId, ct)
                ?? throw AppException.Conflict($"Không tìm thấy cua {line.CrabCode}.");

            if (!line.PendingMoltConfirmed
                && crab.MoltedAt is null
                && now > crab.StockedAt.AddDays(45))
            {
                throw AppException.Conflict(
                    $"Cua {line.CrabCode} đã quá 45 ngày và chưa lột; không thể tiếp tục nuôi.");
            }
        }

        if (harvestedLines.Count > 0)
        {
            if (harvestedLines.Any(line =>
                    !line.HarvestWeightGram.HasValue
                    || line.HarvestWeightGram.Value <= 0))
            {
                throw AppException.Conflict(
                    "Thiếu khối lượng hợp lệ của cua thu hoạch.");
            }

            if (harvestedLines.Any(line =>
                    !line.HarvestCarapaceLengthMm.HasValue
                    || line.HarvestCarapaceLengthMm.Value <= 0
                    || !line.HarvestCarapaceWidthMm.HasValue
                    || line.HarvestCarapaceWidthMm.Value <= 0
                    || !line.PendingHarvestedAt.HasValue))
            {
                throw AppException.Conflict(
                    "Thiếu chiều dài, chiều rộng hoặc thời điểm thu hoạch của cua.");
            }

            var requests = harvestedLines.Select(line =>
                new HarvestLineRequest(
                    CrabId: line.CrabId,
                    WeightGram: line.HarvestWeightGram!.Value,
                    Grade: line.HarvestGrade,
                    IsSoftshell: line.HarvestIsSoftshell ?? true,
                    Notes: line.HarvestNotes,
                    ConditionLabel: line.HarvestConditionLabel,
                    PhotoUrls: JsonStringList.Parse(line.HarvestPhotoUrlsJson),
                    Result: "passed")).ToList();

            _harvestRecorder.ValidateHarvestLines(
                requests,
                requirePositiveWeight: true);

        }

        return (lines, harvestedLines, deferredLines);
    }

    public async Task<ApiResponse<IEnumerable<HarvestAssignmentStaffDto>>>
    GetStaffForAreaAsync(
        Guid farmingAreaId,
        CancellationToken ct = default)
    {
        if (farmingAreaId == Guid.Empty)
            throw AppException.BadRequest("Cần chọn khu nuôi.");

        var manager = await _areas.CurrentUserAsync(ct)
            ?? throw AppException.Unauthorized("Không xác định được Manager.");

        if (manager.Role != UserRole.FarmManager)
            throw AppException.Forbidden("Chỉ Farm Manager được xem nhân viên trong khu.");

        // Chỉ cho phép truy vấn các khu được gán cho Manager hiện tại.
        await _areas.EnsureAsync(farmingAreaId, manage: false, ct);

        var staffIds = (await _uow.FarmStaffAssignments.FindAsync(
                x => x.ManagerUserId == manager.Id
                    && x.FarmingAreaId == farmingAreaId,
                ct))
            .Select(x => x.StaffUserId)
            .Distinct()
            .ToList();

        if (staffIds.Count == 0)
        {
            return ApiResponse<IEnumerable<HarvestAssignmentStaffDto>>.Ok(
                Array.Empty<HarvestAssignmentStaffDto>());
        }

        var staff = await _uow.Users.FindAsync(
            x => staffIds.Contains(x.Id)
                && x.Role == UserRole.FarmStaff
                && x.IsActive,
            ct);

        var result = staff
            .OrderBy(x => x.FullName)
            .Select(x => new HarvestAssignmentStaffDto(
                x.Id,
                x.FullName,
                x.UserCode))
            .ToList();

        return ApiResponse<IEnumerable<HarvestAssignmentStaffDto>>.Ok(result);
    }
    private static string? NormalizeNullable(string? value)
    => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal CalculateTotalWeightKg(
        IEnumerable<HarvestLine> lines)
        => decimal.Round(
            lines.Sum(line => line.WeightGram) / 1000m,
            3,
            MidpointRounding.AwayFromZero);

    private async Task<string> ResolveUserNameAsync(CancellationToken ct)
    {
        var user = await _uow.Users.GetByIdAsync(_currentUser.UserId, ct);
        return string.IsNullOrWhiteSpace(user?.FullName)
            ? "Owner"
            : user.FullName.Trim();
    }
    private static void EnsureAssignmentLocation(HarvestAssignmentLine line, Crab crab)
    {
        if (crab.BoxId is null || crab.BoxId != line.BoxId)
            throw AppException.Conflict($"Cua {line.CrabCode} đã đổi vị trí so với nhiệm vụ; cần đối soát trước khi tiếp tục.");
    }

    private static void EnsureHarvestTime(Crab crab, DateTime at, DateTime now)
    {
        if (at < crab.StockedAt || at > now.AddMinutes(5))
            throw AppException.BadRequest($"Thời điểm thu hoạch của cua {crab.Code} phải sau khi thả và không ở tương lai.");
    }

    private async Task ApplyMoltConfirmationAsync(
    HarvestAssignment assignment,
    HarvestAssignmentLine line,
    DateTime now,
    CancellationToken ct)
    {
        if (!line.PendingMoltConfirmed)
            return;

        var crab = await _uow.Crabs.GetByIdAsync(line.CrabId, ct)
            ?? throw AppException.Conflict($"Không tìm thấy cua {line.CrabCode}.");

        if (crab.Status is not (CrabStatus.Alive or CrabStatus.Molting))
        {
            throw AppException.Conflict(
                $"Cua {line.CrabCode} không còn ở trạng thái hợp lệ để xác nhận lột.");
        }

        var confirmedAt = line.CheckedAt ?? now;
        if (confirmedAt < crab.StockedAt)
        {
            throw AppException.Conflict(
                $"Thời điểm xác nhận lột của cua {line.CrabCode} không hợp lệ.");
        }

        if (crab.MoltedAt.HasValue && confirmedAt < crab.MoltedAt.Value)
        {
            throw AppException.Conflict(
                $"Thời điểm xác nhận lột của cua {line.CrabCode} cũ hơn lần lột trước.");
        }
        var oldStatus = crab.Status;
        var oldCondition = crab.Condition;

        var record = new MoltingRecord
        {
            CrabId = crab.Id,
            BoxId = crab.BoxId,
            MoltTime = confirmedAt,
            WeightAfterGram = line.HarvestWeightGram,
            ShellWidthAfterMm = line.HarvestCarapaceWidthMm,
            ShellLengthAfterMm = line.HarvestCarapaceLengthMm,
            Result = "success",
            Source = "harvest-assignment",
            Notes = line.Notes ?? assignment.AssignmentCode,
            PhotoUrlsJson = line.HarvestPhotoUrlsJson
        };

        await _uow.MoltingRecords.AddAsync(record, ct);

        crab.MoltedAt = confirmedAt;
        crab.MoltingStage = "softshell";
        crab.Condition = CrabCondition.Softshell;
        crab.Status = CrabStatus.Alive;
        _uow.Crabs.Update(crab);

        if (oldStatus != crab.Status || oldCondition != crab.Condition)
        {
            await _uow.CrabStatusHistories.AddAsync(
                new CrabStatusHistory
                {
                    CrabId = crab.Id,
                    OldStatus = oldStatus,
                    NewStatus = crab.Status,
                    OldCondition = oldCondition,
                    NewCondition = crab.Condition,
                    ChangedAt = confirmedAt,
                    Source = "harvest-assignment",
                    Reason = assignment.AssignmentCode,
                    ChangedByUserId = line.CheckedByUserId ?? assignment.AssignedToUserId
                },
                ct);
        }
    }
}