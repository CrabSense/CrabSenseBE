using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/edge/commands")]
[Tags("09. Edge command queue")]
public sealed class EdgeCommandsController : ControllerBase
{
    private readonly IEdgeCommandService _service;
    private readonly IConfiguration _configuration;

    public EdgeCommandsController(
        IEdgeCommandService service,
        IConfiguration configuration)
    {
        _service = service;
        _configuration = configuration;
    }

    [HttpPost]
    [Authorize]
    public Task<IActionResult> Enqueue(
        [FromBody] EnqueueEdgeCommandRequest request,
        CancellationToken ct)
        => Execute(async () =>
        {
            var userId = Guid.TryParse(
                User.FindFirst("sub")?.Value,
                out var id) ? id : (Guid?)null;
            return Ok(await _service.EnqueueAsync(request, userId, ct));
        });

    [HttpGet("pending")]
    [AllowAnonymous]
    public Task<IActionResult> Pending([FromQuery] string deviceCode, CancellationToken ct)
        => Execute(async () =>
        {
            if (!IsEdgeRequest())
                return Unauthorized();
            return Ok(await _service.GetPendingAsync(deviceCode, ct));
        });

    [HttpPost("{id:guid}/ack")]
    [AllowAnonymous]
    public Task<IActionResult> Acknowledge(
        Guid id,
        [FromBody] EdgeCommandAckRequest request,
        CancellationToken ct)
        => Execute(async () =>
        {
            if (!IsEdgeRequest())
                return Unauthorized();
            return Ok(await _service.AcknowledgeAsync(id, request, ct));
        });

    private bool IsEdgeRequest()
    {
        var expected = _configuration["EdgeSync:ApiKey"];
        return string.IsNullOrWhiteSpace(expected)
            || string.Equals(
                Request.Headers["X-Edge-Key"].FirstOrDefault(),
                expected,
                StringComparison.Ordinal);
    }

    private static async Task<IActionResult> Execute(
        Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (CrabSenseBE.Application.Common.AppException ex)
        {
            return new ObjectResult(new { success = false, message = ex.Message })
            {
                StatusCode = ex.StatusCode
            };
        }
    }
}
