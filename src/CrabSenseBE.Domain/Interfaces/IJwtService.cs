using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.Domain.Interfaces;

public sealed record AccessTokenResult(string Token, DateTime ExpiresAt);

public interface IJwtService
{
    AccessTokenResult GenerateAccessToken(AppUser user);
    string GenerateRefreshToken();
    Guid? ValidateAccessToken(string token);
}
