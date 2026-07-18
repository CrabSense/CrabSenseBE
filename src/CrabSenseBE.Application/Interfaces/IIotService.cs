using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;

namespace CrabSenseBE.Application.Interfaces;

public interface IIotService
{
    Task<ApiResponse> IngestSensorDataAsync(SensorDataRequest request, CancellationToken ct = default);
    Task<ApiResponse> IngestSensorDataBatchAsync(SensorDataBatchRequest request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<SensorDataDto>>> GetSensorDataAsync(Guid sensorId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<SensorDto>>> GetSensorsAsync(CancellationToken ct = default);
    Task<ApiResponse<SensorDto>> CreateSensorAsync(CreateSensorRequest request, CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<DeviceDto>>> GetDevicesAsync(CancellationToken ct = default);
    Task<ApiResponse<DeviceDto>> UpdateDeviceStatusAsync(Guid deviceId, UpdateDeviceStatusRequest request, CancellationToken ct = default);

    Task<ApiResponse<string>> ProcessHdf5UploadAsync(Hdf5UploadRequest request, Stream fileStream, CancellationToken ct = default);
}
