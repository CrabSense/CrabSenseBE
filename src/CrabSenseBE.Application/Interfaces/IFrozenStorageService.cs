using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.FrozenStorage;

namespace CrabSenseBE.Application.Interfaces;

public interface IFrozenStorageService
{
    Task<ApiResponse<IEnumerable<FrozenLotDto>>> GetAllAsync(
        CancellationToken ct = default);

    Task<ApiResponse<FrozenLotDto>> GetByIdAsync(
        Guid id,
        CancellationToken ct = default);

    Task<ApiResponse<FrozenLotDto>> CreateAsync(
        CreateFrozenLotRequest request,
        CancellationToken ct = default);

    Task<ApiResponse<FrozenLotDto>> UpdateAsync(
        Guid id,
        UpdateFrozenLotRequest request,
        CancellationToken ct = default);

    Task<ApiResponse> DeleteAsync(
        Guid id,
        CancellationToken ct = default);

    Task<ApiResponse<FrozenInventorySummaryDto>> GetInventorySummaryAsync(
    CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<FrozenStorageAgingDto>>>
    GetStorageAgingAsync(
        CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<ExpiringFrozenLotDto>>> GetExpiringLotsAsync(
    int days,
    CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<FrozenCrabItemDto>>> RegisterCrabItemsAsync(
    Guid lotId,
    RegisterFrozenCrabItemsRequest request,
    CancellationToken ct = default);

    Task<ApiResponse<FrozenCrabItemDto>> GetCrabItemByBarcodeAsync(
        string barcodeValue,
        CancellationToken ct = default);

    Task<byte[]> GetCrabItemBarcodePngAsync(
    string barcodeValue,
    CancellationToken ct = default);
}
