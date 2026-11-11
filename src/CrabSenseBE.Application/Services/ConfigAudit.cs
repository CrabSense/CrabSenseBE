using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>Ghi nhật ký khi một thay đổi cấu hình đã được chấp nhận.</summary>
public static class ConfigAudit
{
    public static async Task WriteAsync(
        IUnitOfWork uow,
        Guid? userId,
        string action,
        string entityType,
        Guid entityId,
        string? details,
        CancellationToken ct)
    {
        if (userId is not Guid id || id == Guid.Empty || uow.OperationLogs is null)
            return;

        await uow.OperationLogs.AddAsync(new OperationLog
        {
            UserId = id,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details
        }, ct);
    }
}
