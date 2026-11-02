using System.Linq.Expressions;
using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public class OperationalEfficiencyReportServiceTests
{
    [Fact]
    public async Task Report_WithCrabLotFilter_CountsOnlyCrabsFromSelectedLot()
    {
        var selectedLotId = Guid.NewGuid();
        var otherLotId = Guid.NewGuid();
        var reportDate = new DateTime(2026, 10, 15);

        var crabs = new List<Crab>
        {
            new()
            {
                Id = Guid.NewGuid(),
                CrabLotId = selectedLotId,
                Code = "CRAB-A1",
                StockedAt = reportDate,
                Status = CrabStatus.Alive
            },
            new()
            {
                Id = Guid.NewGuid(),
                CrabLotId = selectedLotId,
                Code = "CRAB-A2",
                StockedAt = reportDate,
                Status = CrabStatus.Dead
            },
            new()
            {
                Id = Guid.NewGuid(),
                CrabLotId = otherLotId,
                Code = "CRAB-B1",
                StockedAt = reportDate,
                Status = CrabStatus.Harvested
            }
        };

        var crabRepo = CreateCrabRepository(crabs);
        var crabLotRepo = new Mock<IRepository<CrabLot>>();

        crabLotRepo
            .Setup(repo => repo.AnyAsync(
                It.IsAny<Expression<Func<CrabLot, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Crabs).Returns(crabRepo.Object);
        uow.Setup(unit => unit.CrabLots).Returns(crabLotRepo.Object);

        var service = new OperationalEfficiencyReportService(uow.Object);

        var result = await service.GetOperationalEfficiencyReportAsync(
            new OperationalEfficiencyFilterDto(
                reportDate,
                reportDate,
                selectedLotId));

        result.TotalCrabs.Should().Be(2);
        result.AliveCrabs.Should().Be(1);
        result.DeadCrabs.Should().Be(1);
        result.HarvestedCrabs.Should().Be(0);
        result.SurvivalRate.Should().Be(50m);
        result.MortalityRate.Should().Be(50m);

        crabLotRepo.Verify(repo => repo.AnyAsync(
            It.IsAny<Expression<Func<CrabLot, bool>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Report_WithoutCrabLotFilter_CountsCrabsFromAllLots()
    {
        var lotA = Guid.NewGuid();
        var lotB = Guid.NewGuid();
        var reportDate = new DateTime(2026, 10, 15);

        var crabs = new List<Crab>
        {
            new()
            {
                Id = Guid.NewGuid(),
                CrabLotId = lotA,
                Code = "CRAB-A1",
                StockedAt = reportDate,
                Status = CrabStatus.Alive
            },
            new()
            {
                Id = Guid.NewGuid(),
                CrabLotId = lotA,
                Code = "CRAB-A2",
                StockedAt = reportDate,
                Status = CrabStatus.Dead
            },
            new()
            {
                Id = Guid.NewGuid(),
                CrabLotId = lotB,
                Code = "CRAB-B1",
                StockedAt = reportDate,
                Status = CrabStatus.Harvested
            }
        };

        var crabRepo = CreateCrabRepository(crabs);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Crabs).Returns(crabRepo.Object);

        var service = new OperationalEfficiencyReportService(uow.Object);

        var result = await service.GetOperationalEfficiencyReportAsync(
            new OperationalEfficiencyFilterDto(reportDate, reportDate));

        result.TotalCrabs.Should().Be(3);
        result.AliveCrabs.Should().Be(1);
        result.DeadCrabs.Should().Be(1);
        result.HarvestedCrabs.Should().Be(1);
        result.SurvivalRate.Should().BeApproximately(33.33m, 0.01m);
        result.MortalityRate.Should().BeApproximately(33.33m, 0.01m);
    }

    private static Mock<IRepository<Crab>> CreateCrabRepository(
        IEnumerable<Crab> crabs)
    {
        var repo = new Mock<IRepository<Crab>>();

        repo.Setup(repository => repository.Query())
            .Returns(new TestAsyncEnumerable<Crab>(crabs));

        return repo;
    }

   private sealed class TestAsyncEnumerable<T> :
    EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable)
        : base(enumerable)
    {
    }

    public TestAsyncEnumerable(Expression expression)
        : base(expression)
    {
    }

    IAsyncEnumerator<T> IAsyncEnumerable<T>.GetAsyncEnumerator(
        CancellationToken cancellationToken)
    {
        return new TestAsyncEnumerator<T>(
            ((IEnumerable<T>)this).GetEnumerator());
    }

    IQueryProvider IQueryable.Provider =>
        new TestAsyncQueryProvider<T>(this);
}

private sealed class TestAsyncEnumerator<T>(
    IEnumerator<T> innerEnumerator) : IAsyncEnumerator<T>
{
    public T Current => innerEnumerator.Current;

    public ValueTask<bool> MoveNextAsync() =>
        new(innerEnumerator.MoveNext());

    public ValueTask DisposeAsync()
    {
        innerEnumerator.Dispose();
        return ValueTask.CompletedTask;
    }
}

private sealed class TestAsyncQueryProvider<TEntity>(
    IQueryProvider innerProvider) : IAsyncQueryProvider
{
    public IQueryable CreateQuery(Expression expression)
    {
        var elementType = expression.Type.GetGenericArguments()[0];

        return (IQueryable)Activator.CreateInstance(
            typeof(TestAsyncEnumerable<>).MakeGenericType(elementType),
            expression)!;
    }

    public IQueryable<TElement> CreateQuery<TElement>(
        Expression expression)
    {
        return new TestAsyncEnumerable<TElement>(expression);
    }

    public object? Execute(Expression expression) =>
        innerProvider.Execute(expression);

    public TResult Execute<TResult>(Expression expression) =>
        innerProvider.Execute<TResult>(expression);

    public TResult ExecuteAsync<TResult>(
        Expression expression,
        CancellationToken cancellationToken = default)
    {
        var resultType = typeof(TResult).GetGenericArguments()[0];

        var executeMethod = typeof(IQueryProvider)
            .GetMethods()
            .Single(method =>
                method.Name == nameof(IQueryProvider.Execute)
                && method.IsGenericMethod)
            .MakeGenericMethod(resultType);

        var executionResult = executeMethod.Invoke(
            innerProvider,
            new object[] { expression });

        var task = typeof(Task)
            .GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(resultType)
            .Invoke(null, new[] { executionResult });

        return (TResult)task!;
    }
}
}