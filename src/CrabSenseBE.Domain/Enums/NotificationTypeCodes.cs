namespace CrabSenseBE.Domain.Enums;

/// <summary>
/// Mã loại thông báo dùng chung BE ↔ client. Mỗi mã tương ứng 1 dòng NotificationRule.
/// </summary>
public static class NotificationTypeCodes
{
    public const string SensorThreshold = "sensor_threshold";
    public const string SensorDisconnect = "sensor_disconnect";
    public const string DeviceDisconnect = "device_disconnect";
    public const string KioskDisconnect = "kiosk_disconnect";
    public const string RasRelayOn = "ras_relay_on";
    public const string RasRelayOff = "ras_relay_off";
    public const string TankOverflow = "tank_overflow";
    public const string TankEmpty = "tank_empty";

    /// <summary>Loại mặc định khi caller không truyền mã.</summary>
    public const string System = "system";

    /// <summary>Cảnh báo vận hành — chịu NotificationPrefs.WarningsEnabled.</summary>
    public static bool IsWarningType(string? typeCode) => typeCode is
        SensorThreshold or TankOverflow or TankEmpty;
}
