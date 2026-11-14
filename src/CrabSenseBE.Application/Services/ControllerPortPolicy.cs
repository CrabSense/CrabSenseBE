using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
namespace CrabSenseBE.Application.Services;
public static class ControllerPortPolicy
{
    public static async Task<ControllerPort?> ResolveOutputAsync(IUnitOfWork uow, Device device, string? channel, Guid? componentId, CancellationToken ct, bool checkBinding = true)
    {
        if (!int.TryParse(channel, out var number) || number < 1 || number > 32)
            throw AppException.BadRequest("Output channel must be 1–32.");
        if (device.CapabilityVersion == 0)
        {
            if (number > 4) throw AppException.Conflict("Controller chưa báo capability cho channel mở rộng.");
            return null; // preserve legacy metadata only; global hardware lock remains.
        }
        var port = await uow.ControllerPorts.FirstOrDefaultAsync(p => p.DeviceId == device.Id && p.IsPresent &&
            p.ConfigVersion == device.CapabilityVersion && p.PortType == "DIGITAL_OUTPUT" && p.Channel == number, ct)
            ?? throw AppException.Conflict("Controller không báo hỗ trợ output này.");
        if (checkBinding && await uow.RasComponents.AnyAsync(c => c.Id != componentId &&
            (c.ControllerPortId == port.Id || c.RelayDeviceId == device.Id && c.RelayChannel == number.ToString()), ct))
            throw AppException.Conflict("Output đã được liên kết với thiết bị khác.");
        return port;
    }

    // Technical mapping is configuration only. Require the existing deployment
    // lock and a recent authenticated OFF observation before changing current outputs.
    // GPIO OFF is not electrical proof: the technician must still check the load.
    public static async Task RequireSafeConfigurationAsync(IUnitOfWork uow, IConfiguration? configuration,
        Device device, ControllerPort port, CancellationToken ct, bool allowRemoved = false)
    {
        if (!HardwareControlGuard.IsDisabled(configuration))
            throw AppException.Conflict("Disable hardware control before changing controller output configuration.");
        if (port.DeviceId != device.Id || port.PortType != "DIGITAL_OUTPUT")
            throw AppException.Conflict("Output does not belong to this controller.");
        var now = DateTime.UtcNow;
        if (port.IsPresent)
        {
            if (port.ConfigVersion != device.CapabilityVersion || port.ReportedOn != false ||
                port.LastReportedAt < now.AddMinutes(-2) || port.LastReportedAt > now.AddMinutes(1))
                throw AppException.Conflict("A current, recent GPIO OFF observation is required.");
        }
        else if (!allowRemoved)
            throw AppException.Conflict("Output is not present in the current inventory.");

        if (await uow.EdgeCommands.AnyAsync(c => c.DeviceCode == device.DeviceCode &&
            (c.Status == "pending" || c.Status == "delivered") &&
            (c.ExpiresAt == default || c.ExpiresAt > now), ct))
            throw AppException.Conflict("Controller still has an active command.");
    }
}
