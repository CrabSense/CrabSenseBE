namespace CrabSenseBE.Application.Interfaces;

/// <summary>
/// Provides information about the currently authenticated user.
/// </summary>
public interface ICurrentUserService
{
    Guid UserId { get; }
    bool IsAuthenticated { get; }
}
