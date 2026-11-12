using CrabSenseBE.Application.Common;
using Microsoft.Extensions.Configuration;

namespace CrabSenseBE.Application.Services;

/// <summary>Software deployment lock; not a physical emergency stop.</summary>
public static class HardwareControlGuard
{
    // Existing in-process test fixtures may omit configuration. All deployed
    // applications load appsettings.json, with the lock enabled by default.
    public static bool IsDisabled(IConfiguration? configuration) =>
        configuration?.GetValue("EdgeSync:ControlDisabled", false) ?? false;

    public static void RequireEnabled(IConfiguration? configuration)
    {
        if (IsDisabled(configuration))
            throw new AppException("Hardware control is disabled: FE/App-only deployment.", 503);
    }
}
