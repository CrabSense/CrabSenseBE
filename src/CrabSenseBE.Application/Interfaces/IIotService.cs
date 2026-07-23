using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;

namespace CrabSenseBE.Application.Interfaces;

public interface IIotService
{
    Task<ApiResponse> IngestSensorDataAsync(SensorDataRequest request, CancellationToken ct = default);
    Task<ApiResponse> IngestSensorDataBatchAsync(SensorDataBatchRequest request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<SensorDataDto>>> GetSensorDataAsync(Guid sensorId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<SensorDataDto?>> GetLatestSensorDataAsync(Guid sensorId, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<SensorLiveDto>>> GetLiveSnapshotAsync(
        Guid? deviceId = null,
        Guid? farmingAreaId = null,
        CancellationToken ct = default);


    Task<ApiResponse<IEnumerable<SensorDto>>> GetSensorsAsync(Guid? deviceId = null, CancellationToken ct = default);
    Task<ApiResponse<SensorDto>> GetSensorAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<SensorDto>> CreateSensorAsync(CreateSensorRequest request, CancellationToken ct = default);
    Task<ApiResponse<SensorDto>> UpdateSensorAsync(Guid id, UpdateSensorRequest request, CancellationToken ct = default);
    Task<ApiResponse> DeleteSensorAsync(Guid id, CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<DeviceDto>>> GetDevicesAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default);
    Task<ApiResponse<DeviceDto>> GetDeviceAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<DeviceDto>> CreateDeviceAsync(CreateDeviceRequest request, CancellationToken ct = default);
    Task<ApiResponse<DeviceDto>> UpdateDeviceAsync(Guid id, UpdateDeviceRequest request, CancellationToken ct = default);
    Task<ApiResponse> DeleteDeviceAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<DeviceDto>> UpdateDeviceStatusAsync(Guid deviceId, UpdateDeviceStatusRequest request, CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<WaterSystemDto>>> GetWaterSystemsAsync(CancellationToken ct = default);
    Task<ApiResponse<WaterSystemDto>> CreateWaterSystemAsync(CreateWaterSystemRequest request, CancellationToken ct = default);
    Task<ApiResponse<WaterSystemDto>> UpdateWaterSystemAsync(Guid id, UpdateWaterSystemRequest request, CancellationToken ct = default);
    Task<ApiResponse> DeleteWaterSystemAsync(Guid id, CancellationToken ct = default);

    Task<ApiResponse<string>> ProcessHdf5UploadAsync(Hdf5UploadRequest request, Stream fileStream, CancellationToken ct = default);
}
