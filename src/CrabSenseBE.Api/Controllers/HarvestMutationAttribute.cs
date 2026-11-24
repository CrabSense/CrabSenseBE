using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Serialize reservation-changing harvest HTTP actions across API instances.
/// All checks and saves happen under the same PostgreSQL transaction-level lock.
/// The global key also protects voucher-code generation across farming areas.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HarvestMutationAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        if (!db.Database.IsNpgsql())
        {
            // Non-relational test host. Production uses PostgreSQL.
            await next();
            return;
        }
        var ct = context.HttpContext.RequestAborted;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(437190, 3)", ct);
        var result = await next();
        if (result.Exception is null && !result.Canceled
            && (result.Result as Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)?.StatusCode is not >= 400)
            await tx.CommitAsync(ct);
        // Dispose rolls back on exceptions or failed actions.
    }
}
