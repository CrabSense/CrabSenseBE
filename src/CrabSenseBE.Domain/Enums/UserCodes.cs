namespace CrabSenseBE.Domain.Enums;

/// <summary>Mã tài khoản theo role: SAD/FAD/FMN/FST + 5 chữ số.</summary>
public static class UserCodes
{
    public const int DigitCount = 5;

    public static string Prefix(UserRole role) => role switch
    {
        UserRole.SystemAdministrator => "SAD",
        UserRole.FarmAdministrator => "FAD",
        UserRole.FarmManager => "FMN",
        UserRole.FarmStaff => "FST",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Role không có mã tài khoản.")
    };

    /// <summary>Tên sequence trong schema be. Chỉ dùng các tên cố định này.</summary>
    public static string SequenceName(UserRole role) => role switch
    {
        UserRole.SystemAdministrator => "user_code_sad_seq",
        UserRole.FarmAdministrator => "user_code_fad_seq",
        UserRole.FarmManager => "user_code_fmn_seq",
        UserRole.FarmStaff => "user_code_fst_seq",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Role không có dãy mã tài khoản.")
    };

    public static string Format(UserRole role, long number) =>
        $"{Prefix(role)}-{number.ToString().PadLeft(DigitCount, '0')}";
}
