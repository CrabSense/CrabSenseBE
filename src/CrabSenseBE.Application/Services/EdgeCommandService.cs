using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CrabSenseBE.Application.Services;

public sealed class EdgeCommandService : IEdgeCommandService
{
    private readonly IUnitOfWork _uow;
    private readonly FarmAreaScope _areas;
    private readonly ICurrentUserService? _currentUser;
    private readonly IConfiguration? _configuration;

    private void RequireHardwareControl() => HardwareControlGuard.RequireEnabled(_configuration);
    private readonly int _maxAttempts;
    private readonly TimeSpan _commandTtl;
    private readonly TimeSpan _leaseDuration;

    public EdgeCommandService(
        IUnitOfWork uow,
        ICurrentUserService? currentUser = null,
        IConfiguration? configuration = null)
    {
        _uow = uow;
        _currentUser = currentUser;
        _configuration = configuration;
        _areas = new FarmAreaScope(uow, currentUser);
        _maxAttempts = Math.Clamp(
            configuration?.GetValue("EdgeSync:CommandMaxAttempts", 3) ?? 3,
            1,
            10);
        _commandTtl = TimeSpan.FromSeconds(Math.Clamp(
            configuration?.GetValue("EdgeSync:CommandTtlSeconds", 300) ?? 300,
            30,
            3600));
        _leaseDuration = TimeSpan.FromSeconds(Math.Clamp(
            configuration?.GetValue("EdgeSync:CommandLeaseSeconds", 30) ?? 30,
            5,
            300));
    }

    public async Task<ApiResponse<EdgeCommandDto>> EnqueueManualAsync(
        EnqueueEdgeCommandRequest request,
        Guid actorId,
        CancellationToken ct = default)
    {
        RequireHardwareControl();
        if (actorId == Guid.Empty
            || _currentUser is null
            || !_currentUser.IsAuthenticated
            || _currentUser.UserId != actorId)
            throw AppException.Unauthorized("Không xác định được người gửi lệnh.");

        var actor = await _areas.CurrentUserAsync(ct);
        if (actor?.Role is not (UserRole.FarmManager or UserRole.FarmStaff))
            throw AppException.Forbidden("Chỉ Farm Manager hoặc Farm Staff được điều khiển relay.");
        return await EnqueueCoreAsync(
            request, actorId, "manual", requiredAreaId: null, ct);
    }

    public async Task<ApiResponse<EdgeCommandDto>> EnqueueAutomationAsync(
        EnqueueEdgeCommandRequest request,
        Guid farmingAreaId,
        string automationSource,
        CancellationToken ct = default)
    {
        RequireHardwareControl();
        if (farmingAreaId == Guid.Empty)
            throw AppException.Forbidden("Automation phải xác định FarmingArea.");
        if (string.IsNullOrWhiteSpace(automationSource))
            throw AppException.BadRequest("Automation source is required.");

        return await EnqueueCoreAsync(
            request,
            actorId: null,
            $"automation:{automationSource.Trim()}",
            farmingAreaId,
            ct);
    }

    private async Task<ApiResponse<EdgeCommandDto>> EnqueueCoreAsync(
        EnqueueEdgeCommandRequest request,
        Guid? actorId,
        string requestSource,
        Guid? requiredAreaId,
        CancellationToken ct)
    {
        var deviceCode = request.DeviceCode?.Trim();
        var command = request.Command?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(deviceCode))
            throw AppException.BadRequest("DeviceCode is required.");
        if (command is not ("on" or "off" or "alloff"))
            throw AppException.BadRequest("Command must be on, off, or alloff. Use explicit desired state; toggle is unsafe.");

        var device = await _uow.Devices.FirstOrDefaultAsync(d => d.DeviceCode == deviceCode, ct)
            ?? throw AppException.NotFound($"Device '{deviceCode}'");
        var deviceAreaId = await ResolveDeviceAreaAsync(device, ct);
        if (actorId is Guid userId)
        {
            if (userId == Guid.Empty)
                throw AppException.Unauthorized("UserId không hợp lệ.");
            await _areas.EnsureAsync(deviceAreaId, manage: false, ct);
        }
        else if (requiredAreaId != deviceAreaId)
            throw AppException.Forbidden(
                "Automation không được điều khiển thiết bị ngoài khu nguồn.");
        if (device.Status != DeviceStatus.Online
            || device.EdgeState is not ("Approved" or "Online"))
            throw AppException.Conflict("Thiết bị chưa online hoặc chưa được phê duyệt.");

        var channel = command == "alloff" || string.IsNullOrWhiteSpace(request.Channel) ? null : request.Channel.Trim();
        long? inventoryRevision = device.CapabilityVersion > 0 ? device.CapabilityVersion : null;
        if (command is "on" or "off")
        {
            if (channel is null)
                throw AppException.BadRequest("Channel is required for relay commands.");
            if (device.CapabilityVersion > 0)
            {
                var port = await ControllerPortPolicy.ResolveOutputAsync(_uow, device, channel, null, ct, checkBinding: false);
                if (port is null || port.LastReportedAt < DateTime.UtcNow.AddMinutes(-2))
                    throw AppException.Conflict("Port inventory is stale.");
                channel = port.Channel!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                // Safe-off can address an unbound current output. ON preserves
                // main's physical commissioning and exact RAS binding requirement.
                if (command == "on" && (port.CommissioningStatus != "Verified" ||
                    !await _uow.RasComponents.AnyAsync(x => x.ControllerPortId == port.Id &&
                        x.RelayDeviceId == device.Id && x.RelayChannel == channel, ct)))
                    throw AppException.Conflict("Port chưa được nghiệm thu điều khiển.");
            }
            else if (!await _uow.RasComponents.AnyAsync(x => x.RelayDeviceId == device.Id && x.RelayChannel == channel, ct))
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
        {
            if (!string.Equals(duplicate.Command, command, StringComparison.Ordinal)
                || !string.Equals(duplicate.Channel, channel, StringComparison.Ordinal)
                || !string.Equals(duplicate.RequestSource, requestSource, StringComparison.Ordinal)
                || duplicate.RequestedByUserId != actorId
                || duplicate.InventoryRevision != inventoryRevision)
                throw AppException.Conflict(
                    "IdempotencyKey đã được dùng cho payload hoặc actor khác.");
            return ApiResponse<EdgeCommandDto>.Ok(Map(duplicate), "Command already queued.");
        }

        var now = DateTime.UtcNow;
        var entity = new EdgeCommand
        {
            DeviceCode = deviceCode,
            Command = command,
            Channel = channel,
            InventoryRevision = inventoryRevision,
            CorrelationId = correlationId,
            RequestedByUserId = actorId,
            RequestSource = requestSource,
            ExpiresAt = now.Add(_commandTtl)
        };
        await _uow.EdgeCommands.AddAsync(entity, ct);

        if (actorId is Guid auditUserId && auditUserId != Guid.Empty)
        {
            await _uow.OperationLogs.AddAsync(new OperationLog
            {
                UserId = auditUserId,
                Action = $"edge_{command}",
                EntityType = "EdgeCommand",
                EntityId = entity.Id,
                Details = $"{deviceCode}|{entity.Channel}"
            }, ct);
        }
        try { await _uow.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            throw AppException.Conflict("Lệnh đồng thời hoặc trùng idempotency; hãy tải lại trạng thái trước khi thử lại.");
        }

        return ApiResponse<EdgeCommandDto>.Ok(Map(entity), "Command queued.");
    }

    public async Task<ApiResponse<IReadOnlyList<EdgeCommandDto>>> GetPendingAsync(
        string deviceCode,
        string leaseHolder,
        CancellationToken ct = default)
    {
        RequireHardwareControl();
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
            .OrderBy(c => c.CommandSequence)
            .ThenBy(c => c.CreatedAt)
            .Take(20)
            .ToList();

        var commands = new List<EdgeCommand>();
        // The authenticated API already verifies the device. Preserve compatibility
        // for legacy in-process callers while enforcing current inventory at lease time.
        var inventoryDevice = candidates.Count == 0 || _uow.Devices is null ? null :
            await _uow.Devices.FirstOrDefaultAsync(d => d.DeviceCode == code, ct);
        foreach (var command in candidates)
        {
            if (command.ExpiresAt == default)
                command.ExpiresAt = command.CreatedAt.Add(_commandTtl);
            if (command.ExpiresAt <= now || command.AttemptCount >= _maxAttempts)
            {
                command.Status = "timedout";
                command.FailedAt = now;
                command.ResultMessage = command.ExpiresAt <= now
                    ? "Command expired before acknowledgement."
                    : "Maximum delivery attempts exceeded.";
                command.LeaseExpiresAt = null;
                command.DeliveryAttemptId = null;
                command.ConcurrencyVersion++;
                _uow.EdgeCommands.Update(command);
                continue;
            }
            if (command.Status == "delivered" && command.Command == "toggle")
            {
                command.Status = "timedout";
                command.FailedAt = now;
                command.ResultMessage = "Toggle timed out; automatic retry is unsafe.";
                command.LeaseExpiresAt = null;
                command.DeliveryAttemptId = null;
                command.ConcurrencyVersion++;
                _uow.EdgeCommands.Update(command);
                continue;
            }
            if (command.InventoryRevision.HasValue || inventoryDevice?.CapabilityVersion > 0)
            {
                var allowed = inventoryDevice is not null && inventoryDevice.CapabilityVersion > 0
                    && command.InventoryRevision == inventoryDevice.CapabilityVersion;
                if (allowed && command.Command != "alloff")
                {
                    var number = int.TryParse(command.Channel, out var parsed) ? parsed : 0;
                    var port = await _uow.ControllerPorts.FirstOrDefaultAsync(p => p.DeviceId == inventoryDevice!.Id
                        && p.IsPresent && p.ConfigVersion == inventoryDevice.CapabilityVersion
                        && p.PortType == "DIGITAL_OUTPUT" && p.Channel == number, ct);
                    allowed = port is not null && port.LastReportedAt >= now.AddMinutes(-2)
                        && (command.Command == "off" || (command.Command == "on" && port.CommissioningStatus == "Verified"
                            && await _uow.RasComponents.AnyAsync(x => x.ControllerPortId == port.Id
                                && x.RelayDeviceId == inventoryDevice!.Id && x.RelayChannel == command.Channel, ct)));
                }
                if (!allowed)
                {
                    command.Status = "failed";
                    command.FailedAt = now;
                    command.ResultMessage = "Controller inventory changed or port is no longer permitted.";
                    command.LeaseExpiresAt = null;
                    command.DeliveryAttemptId = null;
                    command.ConcurrencyVersion++;
                    _uow.EdgeCommands.Update(command);
                    continue;
                }
            }
            command.Status = "delivered";
            command.DeliveredAt = now;
            command.LastAttemptAt = now;
            command.LeaseExpiresAt = now.Add(_leaseDuration);
            command.LeaseHolder = leaseHolder.Trim();
            command.DeliveryAttemptId = Guid.NewGuid();
            command.AttemptCount++;
            command.ConcurrencyVersion++;
            _uow.EdgeCommands.Update(command);
            commands.Add(command);
        }
        if (candidates.Count > 0)
        {
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw AppException.Conflict(
                    "Command đã được lease bởi một poll đồng thời; hãy poll lại.");
            }
        }

        return ApiResponse<IReadOnlyList<EdgeCommandDto>>.Ok(commands.Select(Map).ToList());
    }

    public async Task<ApiResponse<EdgeCommandDto>> AcknowledgeAsync(
        Guid id,
        EdgeCommandAckRequest request,
        string authenticatedDeviceCode,
        CancellationToken ct = default,
        bool requireDeliveryAttemptId = false)
    {
        RequireHardwareControl();
        var command = await _uow.EdgeCommands.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("EdgeCommand");
        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is not ("acknowledged" or "failed"))
            throw AppException.BadRequest("Status must be acknowledged or failed.");
        if (!string.Equals(command.DeviceCode, authenticatedDeviceCode, StringComparison.OrdinalIgnoreCase))
            throw AppException.Forbidden("Credential không sở hữu command này.");
        if (command.Status != "delivered" || command.LeaseExpiresAt < DateTime.UtcNow)
            throw AppException.Conflict("Command không có delivery lease hợp lệ.");
        if (request.DeliveryAttemptId.HasValue
            && request.DeliveryAttemptId != command.DeliveryAttemptId)
            throw AppException.Conflict("Delivery attempt đã cũ hoặc không hợp lệ.");
        if (requireDeliveryAttemptId && !request.DeliveryAttemptId.HasValue)
            throw AppException.Conflict("DeliveryAttemptId là bắt buộc cho device credential này.");

        command.Status = status;
        command.AcknowledgedAt = DateTime.UtcNow;
        command.FailedAt = status == "failed" ? DateTime.UtcNow : null;
        command.ResultMessage = request.Message?.Trim();
        command.LeaseExpiresAt = null;
        command.DeliveryAttemptId = null;
        command.ConcurrencyVersion++;
        _uow.EdgeCommands.Update(command);
        try { await _uow.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict("Lệnh đã thay đổi hoặc đã bị thay thế.");
        }
        return ApiResponse<EdgeCommandDto>.Ok(Map(command), "Command acknowledgement saved.");
    }

    private static EdgeCommandDto Map(EdgeCommand command) =>
        new(command.Id, command.DeviceCode, command.Command, command.Channel,
            command.Status, command.CreatedAt, command.CorrelationId,
            command.LeaseExpiresAt, command.AttemptCount,
            command.DeliveryAttemptId,
            command.ExpiresAt == default ? null : command.ExpiresAt,
            command.CommandSequence, command.SupersededAt, command.SupersededByCommandId, command.InventoryRevision);

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
