using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public sealed class EdgeCommandService : IEdgeCommandService
{
    private readonly IUnitOfWork _uow;

    public EdgeCommandService(IUnitOfWork uow) => _uow = uow;

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

        _ = await _uow.Devices.FirstOrDefaultAsync(d => d.DeviceCode == deviceCode, ct)
            ?? throw AppException.NotFound($"Device '{deviceCode}'");

        var entity = new EdgeCommand
        {
            DeviceCode = deviceCode,
            Command = command,
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? null : request.Channel.Trim(),
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        await _uow.EdgeCommands.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

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
            await _uow.SaveChangesAsync(ct);
        }

        return ApiResponse<EdgeCommandDto>.Ok(Map(entity), "Command queued.");
    }

    public async Task<ApiResponse<IReadOnlyList<EdgeCommandDto>>> GetPendingAsync(
        string deviceCode,
        CancellationToken ct = default)
    {
        var code = deviceCode?.Trim();
        if (string.IsNullOrWhiteSpace(code))
            throw AppException.BadRequest("deviceCode is required.");

        var commands = (await _uow.EdgeCommands.FindAsync(
                c => c.DeviceCode == code && c.Status == "pending", ct))
            .OrderBy(c => c.CreatedAt)
            .Take(20)
            .ToList();

        var now = DateTime.UtcNow;
        foreach (var command in commands)
        {
            command.Status = "delivered";
            command.DeliveredAt = now;
            _uow.EdgeCommands.Update(command);
        }
        if (commands.Count > 0)
            await _uow.SaveChangesAsync(ct);

        return ApiResponse<IReadOnlyList<EdgeCommandDto>>.Ok(commands.Select(Map).ToList());
    }

    public async Task<ApiResponse<EdgeCommandDto>> AcknowledgeAsync(
        Guid id,
        EdgeCommandAckRequest request,
        CancellationToken ct = default)
    {
        var command = await _uow.EdgeCommands.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("EdgeCommand");
        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is not ("acknowledged" or "failed"))
            throw AppException.BadRequest("Status must be acknowledged or failed.");

        command.Status = status;
        command.AcknowledgedAt = DateTime.UtcNow;
        command.ResultMessage = request.Message?.Trim();
        _uow.EdgeCommands.Update(command);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<EdgeCommandDto>.Ok(Map(command), "Command acknowledgement saved.");
    }

    private static EdgeCommandDto Map(EdgeCommand command) =>
        new(command.Id, command.DeviceCode, command.Command, command.Channel,
            command.Status, command.CreatedAt, command.CorrelationId);
}
