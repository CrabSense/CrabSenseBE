using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Auth;

namespace CrabSenseBE.Application.Interfaces;

public interface IAuthService
{
    Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<ApiResponse<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task<ApiResponse> LogoutAsync(Guid userId, CancellationToken ct = default);
    Task<ApiResponse<UserDto>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<ApiResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
    Task<ApiResponse<UserDto>> GetMeAsync(Guid userId, CancellationToken ct = default);
}
