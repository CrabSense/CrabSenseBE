using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CrabSenseBE.Infrastructure.Services;

/// <summary>
/// Sao lưu / phục hồi schema "be" bằng cách gọi pg_dump / pg_restore.
///
/// Vì sao shell-out thay vì tự serialize trong .NET: pg_dump là công cụ chuẩn,
/// giữ đúng kiểu dữ liệu, sequence, index, constraint — tự viết lại là thừa.
///
/// Yêu cầu: client PostgreSQL >= version server. Supabase đang chạy PG 17,
/// nên máy/container cần pg_dump >= 17 (pg_dump cũ hơn sẽ báo version mismatch).
/// Đặt thư mục chứa pg_dump qua cấu hình "Backup:PgBin" nếu không có trong PATH.
///
/// ponytail: không chạy nền / không lên lịch. Đây là thao tác admin chủ động
/// trước khi sửa dữ liệu — thêm scheduler khi thực sự cần.
/// </summary>
public class DatabaseBackupService : IDatabaseBackupService
{
    /// <summary>Schema EF dùng cho toàn bộ dữ liệu nghiệp vụ (xem AppDbContext.HasDefaultSchema).</summary>
    private const string SchemaName = "be";
    private const string BackupExtension = ".dump";

    /// <summary>Chỉ nhận tên file an toàn — không cho '..' hay dấu phân cách đường dẫn.</summary>
    private static readonly Regex SafeFileName =
        new(@"^[A-Za-z0-9_\-]+\.dump$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(10);

    private readonly string _directory;
    private readonly string? _configuredPgBin;
    private readonly string _host;
    private readonly string _port;
    private readonly string _database;
    private readonly string _username;
    private readonly string _password;
    private readonly string _sslMode;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(IConfiguration config, ILogger<DatabaseBackupService> logger)
    {
        _logger = logger;
        _configuredPgBin = config["Backup:PgBin"];
        _directory = ResolveDirectory(config["Backup:Directory"]);

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Thiếu ConnectionStrings:DefaultConnection.");

        var cs = new NpgsqlConnectionStringBuilder(connectionString);
        _host = cs.Host ?? throw new InvalidOperationException("Connection string thiếu Host.");
        _port = cs.Port.ToString();
        _database = cs.Database ?? "postgres";
        _username = cs.Username ?? throw new InvalidOperationException("Connection string thiếu Username.");
        _password = cs.Password ?? string.Empty;
        _sslMode = ToLibpqSslMode(cs.SslMode);
    }

    /// <summary>
    /// Nơi lưu bản sao lưu. Mặc định nằm NGOÀI thư mục deploy
    /// (deploy-vps.yml chạy `rm -rf /opt/crabsense-api` mỗi lần push main).
    /// </summary>
    private static string ResolveDirectory(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var fallback = OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CrabSense", "backups")
            : "/var/lib/crabsense/backups";

        return fallback;
    }

    // ── API ────────────────────────────────────────────────────────────────────

    public async Task<DatabaseBackupResult> CreateAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        var pgDump = ResolveTool("pg_dump");

        // Tên theo UTC để không lệ thuộc timezone máy chủ.
        var fileName = $"crabsense_{SchemaName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{BackupExtension}";
        var path = Path.Combine(_directory, fileName);

        var args = new List<string>
        {
            "--host", _host,
            "--port", _port,
            "--username", _username,
            "--dbname", _database,
            "--schema", SchemaName,
            "--format", "custom",   // -Fc: nén + pg_restore chọn được từng bảng
            "--no-owner",           // bỏ role Supabase -> restore được ở nơi khác
            "--no-acl",
            "--file", path,
        };

        var sw = Stopwatch.StartNew();
        var (exitCode, _, stdErr) = await RunAsync(pgDump, args, ct);
        sw.Stop();

        if (exitCode != 0)
        {
            TryDelete(path);
            throw new InvalidOperationException(
                $"pg_dump thất bại (exit {exitCode}): {SummarizeError(stdErr, pgDump)}");
        }

        if (!File.Exists(path) || new FileInfo(path).Length < 1024)
        {
            TryDelete(path);
            throw new InvalidOperationException("pg_dump báo hoàn tất nhưng file backup trống hoặc quá nhỏ.");
        }

        var info = Describe(path);
        _logger.LogInformation(
            "Đã tạo backup DB {FileName} ({SizeBytes} bytes) trong {Seconds:0.0}s",
            info.FileName, info.SizeBytes, sw.Elapsed.TotalSeconds);

        return new DatabaseBackupResult(info, sw.Elapsed.TotalSeconds);
    }

    public IReadOnlyList<DatabaseBackupInfo> List()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<DatabaseBackupInfo>();

        return Directory.EnumerateFiles(_directory, $"*{BackupExtension}", SearchOption.TopDirectoryOnly)
            .Where(p => SafeFileName.IsMatch(Path.GetFileName(p)))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(Describe)
            .ToList();
    }

    public string? ResolvePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        // Chuẩn hoá trước: '..\x.dump' hay 'a/b.dump' sẽ khác tên gốc -> bị chặn.
        var name = Path.GetFileName(fileName);
        if (!string.Equals(name, fileName, StringComparison.Ordinal)) return null;
        if (!SafeFileName.IsMatch(name)) return null;

        var full = Path.Combine(_directory, name);
        return File.Exists(full) ? full : null;
    }

    public async Task RestoreAsync(string fileName, CancellationToken ct = default)
    {
        var path = ResolvePath(fileName)
            ?? throw new FileNotFoundException($"Không tìm thấy bản sao lưu '{fileName}'.");

        var pgRestore = ResolveTool("pg_restore");

        var args = new List<string>
        {
            "--host", _host,
            "--port", _port,
            "--username", _username,
            "--dbname", _database,
            "--clean",              // xoá đối tượng cũ trước khi tạo lại
            "--if-exists",
            "--no-owner",
            "--no-acl",
            "--single-transaction", // lỗi giữa đường -> rollback, DB không bị hỏng dở
            path,
        };

        _logger.LogWarning("Bắt đầu phục hồi DB từ {FileName}", fileName);

        var (exitCode, _, stdErr) = await RunAsync(pgRestore, args, ct);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"pg_restore thất bại (exit {exitCode}): {SummarizeError(stdErr, pgRestore)}");
        }

        _logger.LogWarning("Đã phục hồi DB từ {FileName}", fileName);
    }

    // ── Tìm công cụ ────────────────────────────────────────────────────────────

    private string ResolveTool(string name)
    {
        var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;

        // 1) Cấu hình tường minh
        if (!string.IsNullOrWhiteSpace(_configuredPgBin))
        {
            var configured = Path.Combine(_configuredPgBin, exe);
            if (File.Exists(configured)) return configured;
        }

        // 2) Thư mục cài chuẩn, bản major mới nhất trước.
        //    Đặt TRƯỚC PATH vì trên Debian/Ubuntu /usr/bin/pg_dump là pg_wrapper, và
        //    pg_wrapper đời cũ không biết version mới nên vẫn trả về bản cũ nhất.
        var fromKnownDir = FindInKnownDirectories(exe);
        if (fromKnownDir != null) return fromKnownDir;

        // 3) PATH
        var onPath = FindOnPath(exe);
        if (onPath != null) return onPath;

        throw new InvalidOperationException(
            $"Không tìm thấy {name}. Cài PostgreSQL client >= 17, " +
            "hoặc đặt cấu hình Backup:PgBin trỏ tới thư mục bin của client.");
    }

    /// <summary>
    /// Quét các thư mục cài PostgreSQL theo quy ước: &lt;root&gt;/&lt;major&gt;/bin/&lt;tool&gt;.
    /// Chọn major lớn nhất — client mới hơn thì dump được cả server cũ hơn.
    /// </summary>
    private static string? FindInKnownDirectories(string exe)
    {
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            foreach (var folder in new[]
                     {
                         Environment.SpecialFolder.ProgramFiles,
                         Environment.SpecialFolder.ProgramFilesX86,
                     })
            {
                var root = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(root)) continue;

                var pgRoot = Path.Combine(root, "PostgreSQL");
                if (!Directory.Exists(pgRoot)) continue;

                candidates.AddRange(Directory.GetDirectories(pgRoot)
                    .Select(dir => Path.Combine(dir, "bin", exe)));
            }
        }
        else
        {
            // Debian/Ubuntu: /usr/lib/postgresql/17/bin
            AddVersionedCandidates(candidates, "/usr/lib/postgresql", exe);
            // RHEL/Fedora:   /usr/pgsql-17/bin
            if (Directory.Exists("/usr"))
            {
                candidates.AddRange(Directory.GetDirectories("/usr", "pgsql-*")
                    .Select(dir => Path.Combine(dir, "bin", exe)));
            }
        }

        return PickNewestExisting(candidates);
    }

    /// <summary>
    /// Chọn công cụ ở thư mục major lớn nhất trong các đường dẫn còn tồn tại.
    /// Tách riêng để test được: nếu MajorVersionOf trả 0 cho mọi đường dẫn thì
    /// thứ tự thành tuỳ tiện -> chọn nhầm bản cũ mà không báo lỗi.
    /// </summary>
    internal static string? PickNewestExisting(IEnumerable<string> candidates)
        => candidates.Where(File.Exists).OrderByDescending(MajorVersionOf).FirstOrDefault();

    private static void AddVersionedCandidates(List<string> sink, string root, string exe)
    {
        if (!Directory.Exists(root)) return;

        foreach (var dir in Directory.GetDirectories(root))
            sink.Add(Path.Combine(dir, "bin", exe));
    }

    private static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // mục PATH không hợp lệ — bỏ qua
            }
        }
        return null;
    }

    /// <summary>Lấy số major từ tên thư mục kiểu "PostgreSQL\17" để so sánh bản mới hơn.</summary>
    /// <summary>
    /// Lấy số major từ đường dẫn công cụ dạng &lt;...&gt;/&lt;major&gt;/bin/&lt;tool&gt;
    /// (Windows: PostgreSQL\17\bin\pg_dump.exe; Linux: postgresql/17/bin/pg_dump).
    /// </summary>
    internal static int MajorVersionOf(string toolPath)
    {
        var binDir = Path.GetDirectoryName(toolPath);
        if (binDir == null) return 0;

        var versionDir = Path.GetFileName(Path.GetDirectoryName(binDir));
        if (versionDir == null) return 0;

        var m = Regex.Match(versionDir, @"(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : 0;
    }

    // ── Chạy tiến trình ────────────────────────────────────────────────────────

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        // pg_dump/pg_restore lấy thông tin kết nối qua biến môi trường libpq
        // -> mật khẩu không lộ trong command line hay log.
        psi.Environment["PGPASSWORD"] = _password;
        psi.Environment["PGSSLMODE"] = _sslMode;
        psi.Environment["PGCONNECT_TIMEOUT"] = "30";

        using var process = new Process { StartInfo = psi };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException($"Không khởi chạy được {exe}.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ProcessTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException(
                $"{Path.GetFileName(exe)} vượt quá {ProcessTimeout.TotalMinutes:0} phút và đã bị dừng.");
        }

        return (process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // tiến trình đã tự thoát — không cần làm gì
        }
    }

    // ── Tiện ích ───────────────────────────────────────────────────────────────

    private static DatabaseBackupInfo Describe(string path)
    {
        var fi = new FileInfo(path);
        return new DatabaseBackupInfo(fi.Name, fi.Length, fi.LastWriteTimeUtc);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // file dở dang không xoá được — để lại, lần sau ghi đè tên khác
        }
    }

    /// <summary>Rút gọn stderr thành 1 dòng có nghĩa để trả cho admin.</summary>
    private static string SummarizeError(string stdErr, string exe)
    {
        var line = stdErr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)
                              || l.Contains("fatal", StringComparison.OrdinalIgnoreCase))
            ?? stdErr.Trim();

        if (line.Length == 0) line = "(không có thông báo lỗi)";
        if (line.Length > 500) line = line[..500];

        // Lỗi phổ biến nhất khiến thao tác bí ẩn — nói thẳng cách sửa.
        if (line.Contains("version mismatch", StringComparison.OrdinalIgnoreCase))
        {
            line += " — client PostgreSQL trên máy chạy API cũ hơn server (Supabase PG 17). " +
                    "Cần pg_dump/pg_restore >= 17.";
        }

        return $"{Path.GetFileName(exe)}: {line}";
    }

    private static string ToLibpqSslMode(SslMode mode) => mode switch
    {
        SslMode.Disable => "disable",
        SslMode.Allow => "allow",
        SslMode.Prefer => "prefer",
        SslMode.Require => "require",
        SslMode.VerifyCA => "verify-ca",
        SslMode.VerifyFull => "verify-full",
        _ => "prefer",
    };
}
