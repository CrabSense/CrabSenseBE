using CrabSenseBE.Domain;
using FluentAssertions;

namespace CrabSenseBE.UnitTests.Application;

public class RasComponentCatalogTests
{
    [Theory]
    [InlineData("drum", RasComponentCatalog.Filter)]
    [InlineData("skimmer", RasComponentCatalog.Skimmer)]
    [InlineData("bio", RasComponentCatalog.Biofilter)]
    [InlineData("crab_boxes", RasComponentCatalog.Culture)]
    [InlineData("settling", RasComponentCatalog.SettlingTank)]
    [InlineData("uv", RasComponentCatalog.Uv)]
    public void TypeFromCode_MapsKnownCodes(string code, string expected)
        => RasComponentCatalog.TypeFromCode(code).Should().Be(expected);

    [Fact]
    public void DefaultPipeline_HasCultureThenFilterChain()
    {
        RasComponentCatalog.DefaultPipeline.Should().HaveCount(8);
        RasComponentCatalog.DefaultPipeline[0].Code.Should().Be("crab_boxes");
        RasComponentCatalog.DefaultPipeline[1].Code.Should().Be("drum");
        RasComponentCatalog.DefaultPipeline[^1].Code.Should().Be("settling");
    }
}
