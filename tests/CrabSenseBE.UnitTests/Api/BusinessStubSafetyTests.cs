using CrabSenseBE.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.UnitTests.Api;

public sealed class BusinessStubSafetyTests
{
    [Fact]
    public void UnimplementedBusinessApis_DoNotReturnFakeSuccess()
    {
        AssertNotImplemented(new PriceListsController().GetAll());
        AssertNotImplemented(new DeliveriesController().GetAll());
        AssertNotImplemented(new PaymentsController().GetAll());
    }

    private static void AssertNotImplemented(IActionResult result)
    {
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(
            StatusCodes.Status501NotImplemented);
    }
}
