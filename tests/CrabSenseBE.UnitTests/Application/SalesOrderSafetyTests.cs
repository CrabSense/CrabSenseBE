using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Sales;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class SalesOrderSafetyTests
{
    [Fact]
    public async Task CreateOrder_CannotStartAsCancelled()
    {
        var service = new SalesOrderService(
            Mock.Of<IUnitOfWork>(),
            Mock.Of<ICurrentUserService>());
        var request = new CreateSalesOrderRequest(
            null,
            "Customer",
            null,
            null,
            null,
            null,
            "cancelled",
            null,
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            []);

        var act = () => service.CreateOrderAsync(request);

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(400);
    }
}
