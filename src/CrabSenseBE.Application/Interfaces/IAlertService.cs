using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>Ngưỡng + cảnh báo môi trường / mất kết nối.</summary>
public interface IAlertService
{
    Task<ApiResponse<IEnumerable<AlertThresholdDto>>> GetThresholdsAsync(CancellationToken ct = default);
    Task<ApiResponse<AlertThresholdDto>> CreateThresholdAsync(CreateAlertThresholdRequest req, CancellationToken ct = default);
    Task<ApiResponse<AlertThresholdDto>> UpdateThresholdAsync(Guid id, UpdateAlertThresholdRequest req, CancellationToken ct = default);
    Task<ApiResponse> DeleteThresholdAsync(Guid id, CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<AlertDto>>> GetAlertsAsync(
        bool? activeOnly = true,
        Guid? farmingAreaId = null,
        Guid? boxId = null,
        CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<AlertDto>>> GetHistoryAsync(
        int days = 30,
        Guid? farmingAreaId = null,
        CancellationToken ct = default);
    Task<ApiResponse<AlertDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<AlertUnreadCountDto>> GetUnreadCountAsync(CancellationToken ct = default);
    Task<ApiResponse<AlertDto>> AcknowledgeAsync(Guid id, AcknowledgeAlertRequest req, CancellationToken ct = default);
    Task<ApiResponse<AlertDto>> ResolveAsync(Guid id, CancellationToken ct = default);

    /// <summary>So giá trị đo với ngưỡng → tạo Alert nếu vượt.</summary>
    Task EvaluateMeasurementAsync(Sensor sensor, decimal value, CancellationToken ct = default);

    /// <summary>Quét thiết bị/cảm biến mất kết nối quá timeoutMinutes.</summary>
    Task<ApiResponse<int>> CheckDisconnectsAsync(int timeoutMinutes = 15, CancellationToken ct = default);
}
