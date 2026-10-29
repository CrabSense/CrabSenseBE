
namespace CrabSenseBE.Application.DTOs.FarmUsers;

/// <summary>Thông tin tạo Manager/Staff và khu/trang trại được gán cho tài khoản.</summary>
public record CreateFarmUserRequest(
    string Username,
    string Email,
    string Password,
    string FullName,
    string Role,
    Guid FarmingAreaId
);
