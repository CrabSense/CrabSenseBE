using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Common;

/// <summary>
/// Cấp mã cua hệ thống CRAB-0001… dùng chung cho mọi luồng thả cua.
/// <para>
/// <c>Crabs.Code</c> có unique index và không được NULL, mà default của entity là chuỗi rỗng.
/// Luồng mobile (<c>POST /api/boxes/{id}/crabs</c>) từng bỏ qua bước cấp mã, nên con cua đầu tiên
/// lưu với Code = "" còn mọi lần thả sau đó vi phạm IX_Crabs_Code -> 500.
/// </para>
/// </summary>
public static class CrabCodeAllocator
{
    private const string Prefix = "CRAB-";

    /// <summary>Số kế tiếp chưa dùng, tính theo cả Code lẫn Tag để không đụng mã cũ.</summary>
    public static async Task<int> NextNumberAsync(
        IRepository<Crab> crabs, CancellationToken ct = default)
    {
        var all = await crabs.GetAllAsync(ct);
        var used = new HashSet<int>();
        var max = 0;
        foreach (var c in all)
        {
            foreach (var raw in new[] { c.Code, c.Tag })
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!raw.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                // Tag của mobile là CRAB-<epoch millis>: tràn int nên bị bỏ qua, không nhiễm vào dãy.
                if (!int.TryParse(raw[Prefix.Length..], out var n)) continue;
                used.Add(n);
                if (n > max) max = n;
            }
        }

        for (var i = 1; i <= max + 1; i++)
        {
            if (!used.Contains(i)) return i;
        }

        return max + 1;
    }

    /// <summary>Mã dự kiến kế tiếp — chỉ để hiển thị trước, không giữ chỗ.</summary>
    public static async Task<string> PeekNextAsync(
        IRepository<Crab> crabs, CancellationToken ct = default)
        => $"{Prefix}{await NextNumberAsync(crabs, ct):D4}";

    /// <summary>Mã chắc chắn chưa tồn tại trong bảng Crabs.</summary>
    public static async Task<string> AllocateAsync(
        IRepository<Crab> crabs, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = await PeekNextAsync(crabs, ct);
            if (!await crabs.AnyAsync(c => c.Code == code, ct))
                return code;
        }

        throw AppException.Conflict("Could not allocate a unique crab code. Retry.");
    }
}
