using CrabSenseBE.Application.DTOs.Mortality;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// API quản lý ghi nhận cua chết.
/// </summary>
[ApiController]
[Route("api/mortality")]
[Authorize(Roles = AppRoles.FarmPortal)]
public class MortalityController : ControllerBase
{
    private readonly IMortalityService _service;

    public MortalityController(
        IMortalityService service)
    {
        _service = service;
    }

    /// <summary>
    /// Ghi nhận một cá thể cua chết.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmStaff)]
    public async Task<IActionResult> RecordMortality(
        [FromBody] RecordMortalityRequest request,
        CancellationToken cancellationToken)
    {
        // Lấy UserId từ JWT.
        var userIdClaim =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(
                userIdClaim,
                out var userId))
        {
            return Unauthorized();
        }

        var result =
            await _service.RecordMortalityAsync(
                userId,
                request,
                cancellationToken);

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// Lấy danh sách các bản ghi cua chết.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult>
        GetMortalityRecords(
            CancellationToken cancellationToken)
    {
        var result =
            await _service
                .GetMortalityRecordsAsync(
                    cancellationToken);

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>FM sửa sai bản ghi tử vong; thay đổi được lưu vào lịch sử correction.</summary>
    [HttpPost("{id:guid}/correct")]
    [Authorize(Roles = AppRoles.FarmManager)]
    public async Task<IActionResult> CorrectMortality(
        Guid id,
        [FromBody] CorrectMortalityRequest request,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();
        return Ok(new
        {
            success = true,
            data = await _service.CorrectMortalityAsync(
                id, userId, request, cancellationToken)
        });
    }
}