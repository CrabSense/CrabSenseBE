using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.Common;
namespace CrabSenseBE.UnitTests.Api;
public sealed class ControllerManifestTests
{
    [Fact] public void Fifth_output_is_discovered_without_fixed_channel_list()
    {
        var p = ControllerPortsController.Validate(new("ESP","ESP32-V1","2.1",2,
            new[]{new PortManifestItem("OUT-05","DIGITAL_OUTPUT",5,18,ActiveLevel:"HIGH",On:false)}));
        Assert.Equal(5,p[0].Channel);
    }
    [Fact] public void Duplicate_gpio_is_rejected()
    {
        Assert.Throws<AppException>(()=>ControllerPortsController.Validate(new("ESP","ESP32","2.1",1,
            new[]{new PortManifestItem("OUT-01","DIGITAL_OUTPUT",1,4,ActiveLevel:"HIGH"),
                new PortManifestItem("IN-01","DIGITAL_INPUT",1,4,ActiveLevel:"LOW")})));
    }
}
