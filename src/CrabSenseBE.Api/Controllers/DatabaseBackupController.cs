using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Sao lưu / phục hồi dữ liệu — dùng trước khi sửa dữ liệu để còn cứu được.
/// Chỉ SystemAdmin: bản dump chứa toàn bộ dữ liệu nghiệp vụ.
///
/// Luồng dùng:
///   1. POST /api/admin/db/backup            tạo bản sao lưu
///   2. GET  /api/admin/db/backups           xem danh sách (lấy fileName)
///   3. GET  /api/admin/db/backups/{file}    tải về (cất ở máy khác cho chắc)
///   4. POST /api/admin/db/backups/{file}/restore?confirm=RESTORE   phục hồi
/// </summary>
[ApiController]
[Route("api/admin/db")]
[Authorize(Roles = AppRoles.Platform)]
public class DatabaseBackupController : ControllerBase
{
    private readonly IDatabaseBackupService _backup;

    public DatabaseBackupController(IDatabaseBackupService backup)
    {
        _backup = backup;
    }

    /// <summary>Tạo bản sao lưu mới của toàn bộ dữ liệu nghiệp vụ.</summary>
    [HttpPost("backup")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        try
        {
            var result = await _backup.CreateAsync(ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            return Problem(title: "Không tạo được bản sao lưu", detail: ex.Message, statusCode: 500);
        }
    }

    /// <summary>Danh sách các bản sao lưu hiện có, mới nhất trước.</summary>
    [HttpGet("backups")]
    public IActionResult List()
    {
        return Ok(new { success = true, data = _backup.List() });
    }

    /// <summary>Tải một bản sao lưu về máy.</summary>
    [HttpGet("backups/{fileName}")]
    public IActionResult Download(string fileName)
    {
        var path = _backup.ResolvePath(fileName);
        if (path == null)
            return NotFound(new { success = false, message = $"Không tìm thấy bản sao lưu '{fileName}'." });

        return PhysicalFile(path, "application/octet-stream", Path.GetFileName(path));
    }

    /// <summary>
    /// Phục hồi dữ liệu từ bản sao lưu. Ghi đè dữ liệu hiện tại.
    /// Bắt buộc truyền confirm=RESTORE để tránh gọi nhầm.
    /// </summary>
    [HttpPost("backups/{fileName}/restore")]
    public async Task<IActionResult> Restore(
        string fileName,
        [FromQuery] string? confirm,
        CancellationToken ct)
    {
        if (!string.Equals(confirm, "RESTORE", StringComparison.Ordinal))
        {
            return BadRequest(new
            {
                success = false,
                message = "Thao tác phục hồi ghi đè dữ liệu hiện tại. " +
                          "Thêm ?confirm=RESTORE vào URL nếu chắc chắn."
            });
        }

        try
        {
            await _backup.RestoreAsync(fileName, ct);
            return Ok(new
            {
                success = true,
                data = new { fileName, restoredAtUtc = DateTime.UtcNow }
            });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            return Problem(title: "Không phục hồi được", detail: ex.Message, statusCode: 500);
        }
    }
}
