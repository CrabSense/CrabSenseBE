using System.Security.Claims;
using CrabSenseBE.Application.DTOs.TrainingData;
using CrabSenseBE.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/training-data")]
[Authorize]
[Produces("application/json")]
public class TrainingDataController : ControllerBase
{
    private readonly ITrainingDataService _service;
    public TrainingDataController(ITrainingDataService service) => _service = service;

    [HttpPost("feeding-events")]
    public async Task<IActionResult> CreateFeeding(
        CreateFeedingEventRequest request, CancellationToken ct)
        => Ok(await _service.CreateFeedingAsync(request, CurrentUserId(), ct));

    [HttpPost("observation-events")]
    public async Task<IActionResult> CreateObservation(
        CreateObservationEventRequest request, CancellationToken ct)
        => Ok(await _service.CreateObservationAsync(request, ct));

    [HttpPost("labels")]
    public async Task<IActionResult> Label(
        SubmitTrainingLabelRequest request, CancellationToken ct)
        => Ok(await _service.LabelAsync(request, CurrentUserId(), ct));

    [HttpGet("crabs/{crabId:guid}")]
    public async Task<IActionResult> List(Guid crabId, CancellationToken ct)
        => Ok(await _service.ListAsync(crabId, ct));

    private Guid? CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
