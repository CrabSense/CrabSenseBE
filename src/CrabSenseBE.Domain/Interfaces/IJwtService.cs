using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.Domain.Interfaces;

public interface IJwtService
{
    string GenerateAccessToken(AppUser user);
    string GenerateRefreshToken();
    Guid? ValidateAccessToken(string token);
}
