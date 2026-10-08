using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Kiosk;

namespace CrabSenseBE.Application.Interfaces;

public interface IKioskProvisioningService
{
    Task<ApiResponse<KioskCreatedDto>> CreateAsync(CreateKioskRequest request, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<KioskListItemDto>>> ListAsync(Guid farmingAreaId, CancellationToken ct = default);
    Task<ApiResponse<KioskCreatedDto>> IssueCodeAsync(Guid kioskId, CancellationToken ct = default);
    Task<ApiResponse<RedeemKioskDto>> RedeemAsync(RedeemKioskRequest request, CancellationToken ct = default);
    Task<ApiResponse<KioskSessionDto>> TouchAsync(string secret, string? lanIp, CancellationToken ct = default);
    Task<ApiResponse<KioskListItemDto>> RevokeAsync(Guid kioskId, CancellationToken ct = default);
}
