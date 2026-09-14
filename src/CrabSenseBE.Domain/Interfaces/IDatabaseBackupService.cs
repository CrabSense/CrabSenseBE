namespace CrabSenseBE.Domain.Interfaces;

/// <summary>
/// Thông tin một bản sao lưu DB (file .dump trên đĩa).
/// </summary>
public record DatabaseBackupInfo(
    string FileName,
    long SizeBytes,
    DateTime CreatedAtUtc);

/// <summary>
/// Kết quả tạo bản sao lưu.
/// </summary>
public record DatabaseBackupResult(
    DatabaseBackupInfo Info,
    double DurationSeconds);

/// <summary>
/// Sao lưu / phục hồi toàn bộ schema "be" bằng pg_dump / pg_restore.
/// Dùng trước khi sửa dữ liệu để còn cứu được.
///
/// Chỉ dành cho SystemAdmin — bản dump chứa toàn bộ dữ liệu nghiệp vụ.
/// </summary>
public interface IDatabaseBackupService
{
    /// <summary>Tạo bản sao lưu mới và trả về thông tin file.</summary>
    Task<DatabaseBackupResult> CreateAsync(CancellationToken ct = default);

    /// <summary>Liệt kê các bản sao lưu hiện có, mới nhất trước.</summary>
    IReadOnlyList<DatabaseBackupInfo> List();

    /// <summary>
    /// Đường dẫn tuyệt đối của file backup.
    /// Trả null nếu tên không hợp lệ (chặn path traversal) hoặc file không tồn tại.
    /// </summary>
    string? ResolvePath(string fileName);

    /// <summary>
    /// Phục hồi DB từ file backup: xoá đối tượng cũ rồi tạo lại trong 1 transaction.
    /// Ném exception nếu thất bại (khi đó transaction rollback, DB giữ nguyên).
    /// </summary>
    Task RestoreAsync(string fileName, CancellationToken ct = default);
}
