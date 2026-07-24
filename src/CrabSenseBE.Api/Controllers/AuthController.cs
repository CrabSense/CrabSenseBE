using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;
using CrabSenseBE.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Tags("00. Auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUser;

    public AuthController(IAuthService authService, ICurrentUserService currentUser)
    {
        _authService = authService;
        _currentUser = currentUser;
    }

    /// <summary>[AUTH] Login — get JWT accessToken</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), 200)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
        => Ok(await _authService.LoginAsync(request, ct));

    /// <summary>[CREATE] Register user — Role: SystemAdmin | FarmOwner | Staff</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
        => Ok(await _authService.RegisterAsync(request, ct));

    /// <summary>[AUTH] Refresh token</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
        => Ok(await _authService.RefreshTokenAsync(request, ct));

    /// <summary>[AUTH] Logout</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
        => Ok(await _authService.LogoutAsync(_currentUser.UserId, ct));

    /// <summary>[READ] Current logged-in user</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
        => Ok(await _authService.GetMeAsync(_currentUser.UserId, ct));

    /// <summary>[UPDATE] Update current user profile</summary>
    [HttpPut("me")]
    [Authorize]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken ct)
        => Ok(await _authService.UpdateProfileAsync(_currentUser.UserId, request, ct));

    /// <summary>[UPDATE] Change password</summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
        => Ok(await _authService.ChangePasswordAsync(_currentUser.UserId, request, ct));

    /// <summary>[READ] Per-user notification preferences (Account tab)</summary>
    [HttpGet("me/notification-preferences")]
    [Authorize]
    public async Task<IActionResult> GetNotificationPreferences(CancellationToken ct)
        => Ok(await _authService.GetNotificationPreferencesAsync(_currentUser.UserId, ct));

    /// <summary>[UPDATE] Per-user notification preferences (Account tab)</summary>
    [HttpPut("me/notification-preferences")]
    [Authorize]
    public async Task<IActionResult> UpdateNotificationPreferences(
        [FromBody] NotificationPreferencesDto request, CancellationToken ct)
        => Ok(await _authService.UpdateNotificationPreferencesAsync(_currentUser.UserId, request, ct));
}
