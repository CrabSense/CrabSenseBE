
namespace CrabSenseBE.Application.DTOs.FarmUsers;

public record CreateFarmUserRequest(
    string Username,
    string Email,
    string Password,
    string FullName,
    string Role
);
