using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;

namespace CrabSenseBE.Application.Interfaces;

public interface IHarvestAssignmentService
{
    Task<ApiResponse<HarvestAssignmentDto>> CreateAssignmentAsync(
        CreateHarvestAssignmentRequest request,
        CancellationToken ct = default);

    Task<ApiResponse<HarvestAssignmentResultDto>> SubmitAssignmentResultsAsync(
        Guid assignmentId,
        SubmitHarvestAssignmentResultsRequest request,
        CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<HarvestAssignmentListItemDto>>> GetAssignmentsAsync(
    Guid? farmingAreaId = null,
    CancellationToken ct = default);

    Task<ApiResponse<HarvestAssignmentDetailDto>> GetAssignmentByIdAsync(
        Guid assignmentId,
        CancellationToken ct = default);

    Task<ApiResponse<HarvestAssignmentDto>> AcceptAssignmentAsync(
    Guid assignmentId,
    CancellationToken ct = default);

    Task<ApiResponse<HarvestAssignmentResultDto>> CompleteAssignmentAsync(
    Guid assignmentId,
    CompleteHarvestAssignmentRequest request,
    CancellationToken ct = default);

    Task<ApiResponse<HarvestAssignmentResultDto>> ConfirmAssignmentAsync(Guid assignmentId, ReviewHarvestAssignmentRequest request, CancellationToken ct = default);

    Task<ApiResponse<HarvestAssignmentResultDto>> ReturnAssignmentAsync(Guid assignmentId, ReviewHarvestAssignmentRequest request, CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<HarvestAssignmentStaffDto>>> GetStaffForAreaAsync(
    Guid farmingAreaId,
    CancellationToken ct = default);
}