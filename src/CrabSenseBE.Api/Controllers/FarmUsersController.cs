
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.DTOs.FarmUsers;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// FarmAdministrator creates FarmManager/FarmStaff accounts.
/// Uses the existing AuthService registration rules.
/// </summary>
[ApiController]
[Route("api/farm-users")]
[Authorize(Roles = AppRoles.FarmAdministrator)]
[Tags("01. Farm User Management")]
[Produces("application/json")]
public class FarmUsersController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUser;

    public FarmUsersController(IAuthService authService, ICurrentUserService currentUser)
    {
        _authService = authService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// [CREATE] Create FarmManager or FarmStaff account.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(
        typeof(ApiResponse<UserDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateFarmUserRequest request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest(new
            {
                success = false,
                message = "Request is required."
            });
        }

        // Only allow employee roles.
        if (request.Role != AppRoles.FarmManager &&
            request.Role != AppRoles.FarmStaff)
        {
            return BadRequest(new
            {
                success = false,
                message =
                    "Only FarmManager or FarmStaff can be created."
            });
        }

        // Chuyển DTO chuyên biệt sang request dùng chung; FarmingAreaId là khu được gán cho nhân viên.
        var registerRequest = new RegisterRequest(
            request.Username,
            request.Email,
            request.Password,
            request.FullName,
            request.Role,
            request.FarmingAreaId
        );

        var result = await _authService.RegisterAsync(
            registerRequest,
            UserRole.FarmAdministrator,
            _currentUser.UserId,
            ct
        );

        return Ok(result);
    }
}
