using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CrabSenseBE.Api.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User
                ?? throw AppException.Unauthorized(
                    "Authenticated user context is unavailable.");

            var rawUserId =
                user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(rawUserId, out var userId))
            {
                throw AppException.Unauthorized(
                    "Authenticated user identifier is missing or invalid.");
            }

            return userId;
        }
    }
}
