using CrabSenseBE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrabSenseBE.UnitTests.Infrastructure;

/// <summary>
/// Bảo vệ phần dễ hỏng nguy hiểm nhất của tính năng backup: tên file do client gửi lên.
/// Nếu guard này vỡ, kẻ tấn công đọc được file bất kỳ trên máy chủ qua endpoint tải backup.
/// </summary>
public class DatabaseBackupServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "cs-backup-test-" + Guid.NewGuid().ToString("N"));

    private DatabaseBackupService Create()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Port=5432;Database=x;Username=u;Password=p",
                ["Backup:Directory"] = _dir,
            })
            .Build();

        return new DatabaseBackupService(config, NullLogger<DatabaseBackupService>.Instance);
    }

    private string CreateBackupFile(string name)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "noi dung gia");
        return path;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..\\..\\appsettings.Local.json")]   // thoat bang dau \\ (Windows)
    [InlineData("../../appsettings.Local.json")]     // thoat bang dau /
    [InlineData("sub/a.dump")]                       // duong dan con
    [InlineData("sub\\a.dump")]
    [InlineData("a.sql")]                            // sai phan mo rong
    [InlineData("a.dump.bak")]
    [InlineData("a dump.dump")]                      // khoang trang
    [InlineData("../a.dump")]
    [InlineData("C:\\Windows\\win.ini")]
    public void ResolvePath_TenKhongHopLe_TraVeNull(string? fileName)
    {
        Create().ResolvePath(fileName!).Should().BeNull();
    }

    [Fact]
    public void ResolvePath_TenHopLeVaCoFile_TraVeDuongDanTuyetDoi()
    {
        var created = CreateBackupFile("crabsense_be_20260101_000000.dump");

        var resolved = Create().ResolvePath("crabsense_be_20260101_000000.dump");

        resolved.Should().Be(created);
    }

    [Fact]
    public void ResolvePath_FileKhongTonTai_TraVeNull()
    {
        Create().ResolvePath("crabsense_be_20260101_000000.dump").Should().BeNull();
    }

    [Fact]
    public void List_ThuMucChuaTonTai_TraVeRong()
    {
        Create().List().Should().BeEmpty();
    }

    [Fact]
    public void List_ChiTraVeFileDump_MoiNhatTruoc()
    {
        var older = CreateBackupFile("crabsense_be_20260101_000000.dump");
        var newer = CreateBackupFile("crabsense_be_20260102_000000.dump");
        File.WriteAllText(Path.Combine(_dir, "khong-phai-dump.txt"), "rac");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var list = Create().List();

        list.Should().HaveCount(2);
        list[0].FileName.Should().Be("crabsense_be_20260102_000000.dump");
        list[0].SizeBytes.Should().BeGreaterThan(0);
    }

    // ── Chọn phiên bản pg_dump/pg_restore ──────────────────────────────────────
    // VPS Ubuntu: /usr/bin/pg_dump là pg_wrapper đời cũ, vẫn trả về client 14 dù đã
    // cài client 17. Nếu bước chọn phiên bản sai, BE dump nhầm bằng bản 14 và
    // Supabase (PG 17) từ chối -> tính năng backup chết ở production.

    [Theory]
    [InlineData("/usr/lib/postgresql/17/bin/pg_dump", 17)]
    [InlineData("/usr/lib/postgresql/14/bin/pg_dump", 14)]
    [InlineData("/usr/pgsql-17/bin/pg_dump", 17)]
    [InlineData(@"C:\Program Files\PostgreSQL\17\bin\pg_dump.exe", 17)]
    [InlineData(@"C:\Program Files (x86)\PostgreSQL\16\bin\pg_dump.exe", 16)]
    [InlineData("/usr/bin/pg_dump", 0)]                     // pg_wrapper: khong co so major
    public void MajorVersionOf_DocDungSoMajor(string path, int expected)
    {
        DatabaseBackupService.MajorVersionOf(path).Should().Be(expected);
    }

    [Fact]
    public void PickNewestExisting_ChonMajorLonNhat()
    {
        var root = Path.Combine(_dir, "postgresql");
        var v14 = Path.Combine(root, "14", "bin", "pg_dump");
        var v17 = Path.Combine(root, "17", "bin", "pg_dump");
        Directory.CreateDirectory(Path.GetDirectoryName(v14)!);
        Directory.CreateDirectory(Path.GetDirectoryName(v17)!);
        File.WriteAllText(v14, "cu");
        File.WriteAllText(v17, "moi");

        // Dua 14 len truoc: ket qua khong duoc phu thuoc thu tu dau vao.
        DatabaseBackupService
            .PickNewestExisting(new[] { v14, v17, Path.Combine(root, "18", "bin", "pg_dump") })
            .Should().Be(v17);
    }

    [Fact]
    public void PickNewestExisting_KhongCoFileNaoTonTai_TraVeNull()
    {
        DatabaseBackupService
            .PickNewestExisting(new[] { Path.Combine(_dir, "khong-ton-tai", "pg_dump") })
            .Should().BeNull();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // thu muc tam khong xoa duoc — khong lam hong test
        }
        GC.SuppressFinalize(this);
    }
}
