using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
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
}
