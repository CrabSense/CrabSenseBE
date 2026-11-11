using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public sealed class EdgeCommandService : IEdgeCommandService
{
    private readonly IUnitOfWork _uow;
    private readonly FarmAreaScope _areas;

    public EdgeCommandService(IUnitOfWork uow, ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public async Task<ApiResponse<EdgeCommandDto>> EnqueueAsync(
        EnqueueEdgeCommandRequest request,
        Guid? actorId = null,
        CancellationToken ct = default)
    {
        var deviceCode = request.DeviceCode?.Trim();
        var command = request.Command?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(deviceCode))
            throw AppException.BadRequest("DeviceCode is required.");
        if (command is not ("on" or "off" or "toggle" or "alloff"))
            throw AppException.BadRequest("Command must be on, off, toggle, or alloff.");

        var device = await _uow.Devices.FirstOrDefaultAsync(d => d.DeviceCode == deviceCode, ct)
            ?? throw AppException.NotFound($"Device '{deviceCode}'");
        if (actorId is Guid)
        {
            var areaId = await ResolveDeviceAreaAsync(device, ct);
            await _areas.EnsureAsync(areaId, manage: false, ct);
        }
        if (device.Status != DeviceStatus.Online
            || device.EdgeState is not ("Approved" or "Online"))
            throw AppException.Conflict("Thiết bị chưa online hoặc chưa được phê duyệt.");

        var channel = string.IsNullOrWhiteSpace(request.Channel) ? null : request.Channel.Trim();
        if (command is "on" or "off" or "toggle")
        {
            if (channel is null)
                throw AppException.BadRequest("Channel is required for relay commands.");
            var validChannel = await _uow.RasComponents.AnyAsync(
                component => component.RelayDeviceId == device.Id
                             && component.RelayChannel == channel, ct);
            if (!validChannel)
                throw AppException.BadRequest("Channel không thuộc thiết bị này.");
        }

        var correlationId = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : request.IdempotencyKey.Trim();
        if (correlationId.Length > 100)
            throw AppException.BadRequest("IdempotencyKey tối đa 100 ký tự.");
        var duplicate = await _uow.EdgeCommands.FirstOrDefaultAsync(
            c => c.DeviceCode == deviceCode && c.CorrelationId == correlationId, ct);
        if (duplicate is not null)
            return ApiResponse<EdgeCommandDto>.Ok(Map(duplicate), "Command already queued.");

        var entity = new EdgeCommand
        {
            DeviceCode = deviceCode,
            Command = command,
            Channel = channel,
            CorrelationId = correlationId,
            RequestedByUserId = actorId
        };
        await _uow.EdgeCommands.AddAsync(entity, ct);

        if (actorId is Guid userId && userId != Guid.Empty)
        {
            await _uow.OperationLogs.AddAsync(new OperationLog
            {
                UserId = userId,
                Action = $"edge_{command}",
                EntityType = "EdgeCommand",
                EntityId = entity.Id,
                Details = $"{deviceCode}|{entity.Channel}"
            }, ct);
        }
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<EdgeCommandDto>.Ok(Map(entity), "Command queued.");
    }

    public async Task<ApiResponse<IReadOnlyList<EdgeCommandDto>>> GetPendingAsync(
        string deviceCode,
        string leaseHolder,
        CancellationToken ct = default)
    {
        var code = deviceCode?.Trim();
        if (string.IsNullOrWhiteSpace(code))
            throw AppException.BadRequest("deviceCode is required.");

        if (string.IsNullOrWhiteSpace(leaseHolder))
            throw AppException.Unauthorized("Không xác định được edge credential.");
        var now = DateTime.UtcNow;
        var candidates = (await _uow.EdgeCommands.FindAsync(
                c => c.DeviceCode == code
                     && (c.Status == "pending"
                         || (c.Status == "delivered" && c.LeaseExpiresAt < now)), ct))
            .OrderBy(c => c.CreatedAt)
            .Take(20)
            .ToList();

        var commands = new List<EdgeCommand>();
        foreach (var command in candidates)
        {
            if (command.Status == "delivered" && command.Command == "toggle")
            {
                command.Status = "timedout";
                command.FailedAt = now;
                command.ResultMessage = "Toggle timed out; automatic retry is unsafe.";
                command.ConcurrencyVersion++;
                _uow.EdgeCommands.Update(command);
                continue;
            }
            command.Status = "delivered";
            command.DeliveredAt = now;
            command.LastAttemptAt = now;
            command.LeaseExpiresAt = now.AddSeconds(30);
            command.LeaseHolder = leaseHolder.Trim();
            command.AttemptCount++;
            command.ConcurrencyVersion++;
            _uow.EdgeCommands.Update(command);
            commands.Add(command);
        }
        if (candidates.Count > 0)
            await _uow.SaveChangesAsync(ct);

        return ApiResponse<IReadOnlyList<EdgeCommandDto>>.Ok(commands.Select(Map).ToList());
    }

    public async Task<ApiResponse<EdgeCommandDto>> AcknowledgeAsync(
        Guid id,
        EdgeCommandAckRequest request,
        string authenticatedDeviceCode,
        CancellationToken ct = default)
    {
        var command = await _uow.EdgeCommands.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("EdgeCommand");
        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is not ("acknowledged" or "failed"))
            throw AppException.BadRequest("Status must be acknowledged or failed.");
        if (!string.Equals(command.DeviceCode, authenticatedDeviceCode, StringComparison.OrdinalIgnoreCase))
            throw AppException.Forbidden("Credential không sở hữu command này.");
        if (command.Status != "delivered" || command.LeaseExpiresAt < DateTime.UtcNow)
            throw AppException.Conflict("Command không có delivery lease hợp lệ.");

        command.Status = status;
        command.AcknowledgedAt = DateTime.UtcNow;
        command.FailedAt = status == "failed" ? DateTime.UtcNow : null;
        command.ResultMessage = request.Message?.Trim();
        command.LeaseExpiresAt = null;
        command.ConcurrencyVersion++;
        _uow.EdgeCommands.Update(command);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<EdgeCommandDto>.Ok(Map(command), "Command acknowledgement saved.");
    }

    private static EdgeCommandDto Map(EdgeCommand command) =>
        new(command.Id, command.DeviceCode, command.Command, command.Channel,
            command.Status, command.CreatedAt, command.CorrelationId,
            command.LeaseExpiresAt, command.AttemptCount);

    private async Task<Guid> ResolveDeviceAreaAsync(Device device, CancellationToken ct)
    {
        if (device.FarmingAreaId is Guid areaId) return areaId;
        if (device.FarmingRowId is Guid rowId)
        {
            var row = await _uow.FarmingRows.GetByIdAsync(rowId, ct)
                ?? throw AppException.NotFound("FarmingRow");
            return row.FarmingAreaId;
        }
        if (device.KioskId is Guid kioskId)
        {
            var kiosk = await _uow.FarmKiosks.GetByIdAsync(kioskId, ct)
                ?? throw AppException.NotFound("FarmKiosk");
            return kiosk.FarmingAreaId;
        }
        throw AppException.Forbidden("Không xác định được khu của thiết bị.");
    }
}
