using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.TrainingData;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;
using System.Linq.Expressions;

namespace CrabSenseBE.UnitTests.Application;

public class TrainingDataIntegrityTests
{
    [Fact]
    public async Task CreateFeeding_WhenRequestBoxDoesNotMatchCrab_RejectsWithoutWrite()
    {
        var crab = new Crab { Id = Guid.NewGuid(), BoxId = Guid.NewGuid() };
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.GetByIdAsync(crab.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(crab);
        var feedings = new Mock<IRepository<FeedingEvent>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Crabs).Returns(crabs.Object);
        uow.Setup(x => x.FeedingEvents).Returns(feedings.Object);

        var action = () => new TrainingDataService(uow.Object).CreateFeedingAsync(
            new CreateFeedingEventRequest(
                crab.Id, Guid.NewGuid(), null, null, null, null),
            Guid.NewGuid(),
            CancellationToken.None);

        (await action.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(409);
        feedings.Verify(
            r => r.AddAsync(It.IsAny<FeedingEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Feeding_rejects_future_time_before_write()
    {
        var crab = new Crab { Id = Guid.NewGuid(), BoxId = Guid.NewGuid() };
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.Crabs).Returns(Repository(crab).Object);
        var act = () => new TrainingDataService(uow.Object).CreateFeedingAsync(
            new CreateFeedingEventRequest(crab.Id, crab.BoxId, DateTime.UtcNow.AddDays(1), "feed", 10, null),
            Guid.NewGuid(), CancellationToken.None);
        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(400);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Label_WhenBothSourcesProvided_IsRejected()
    {
        var uow = new Mock<IUnitOfWork>();
        var action = () => new TrainingDataService(uow.Object).LabelAsync(
            new SubmitTrainingLabelRequest(
                Guid.NewGuid(), Guid.NewGuid(), true, null, null, null, null),
            Guid.NewGuid(),
            CancellationToken.None);

        (await action.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(400);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_CrabWithoutObservation_DoesNotUseAnotherFeedingLabel()
    {
        var crabA = new Crab { Id = Guid.NewGuid() };
        var boxA = Guid.NewGuid();
        var feedingA = new FeedingEvent
        {
            Id = Guid.NewGuid(),
            CrabId = crabA.Id,
            BoxId = boxA,
            FedAt = DateTime.UtcNow.AddHours(-1)
        };
        var foreignLabel = new TrainingLabel
        {
            Id = Guid.NewGuid(),
            FeedingEventId = Guid.NewGuid(),
            ObservationEventId = null,
            ConsumptionLevel = "HIGH",
            Ate = true,
            LabeledAt = DateTime.UtcNow
        };
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.Crabs).Returns(Repository(crabA).Object);
        uow.SetupGet(x => x.FeedingEvents).Returns(Repository(feedingA).Object);
        uow.SetupGet(x => x.ObservationEvents)
            .Returns(Repository<ObservationEvent>().Object);
        uow.SetupGet(x => x.TrainingLabels)
            .Returns(Repository(foreignLabel).Object);

        var result = await new TrainingDataService(uow.Object)
            .ListAsync(crabA.Id, CancellationToken.None);

        result.Data.Should().ContainSingle();
        result.Data![0].ConsumptionLevel.Should().BeNull();
        result.Data[0].Ate.Should().BeNull();
    }

    [Fact]
    public async Task CreateHistoricalFeeding_UsesAllocationAtEventTime()
    {
        var historicalBox = Guid.NewGuid();
        var crab = new Crab
        {
            Id = Guid.NewGuid(),
            BoxId = Guid.NewGuid()
        };
        var eventAt = DateTime.UtcNow.AddDays(-2);
        var allocation = new CrabBoxAllocation
        {
            CrabId = crab.Id,
            BoxId = historicalBox,
            StartTime = eventAt.AddDays(-1),
            EndTime = eventAt.AddDays(1)
        };
        var feedings = Repository<FeedingEvent>();
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.Crabs).Returns(Repository(crab).Object);
        uow.SetupGet(x => x.CrabBoxAllocations)
            .Returns(Repository(allocation).Object);
        uow.SetupGet(x => x.FeedingEvents).Returns(feedings.Object);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await new TrainingDataService(uow.Object).CreateFeedingAsync(
            new CreateFeedingEventRequest(
                crab.Id,
                historicalBox,
                eventAt,
                "feed",
                10,
                null),
            Guid.NewGuid(),
            CancellationToken.None);

        feedings.Verify(repo => repo.AddAsync(
            It.Is<FeedingEvent>(feeding =>
                feeding.BoxId == historicalBox
                && feeding.FedAt == eventAt),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IRepository<T>> Repository<T>(params T[] rows)
        where T : class
    {
        var data = rows.ToList();
        var repository = new Mock<IRepository<T>>();
        repository.Setup(repo => repo.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                data.FirstOrDefault(item =>
                    (Guid?)item.GetType().GetProperty("Id")?.GetValue(item)
                    == id));
        repository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                Expression<Func<T, bool>> predicate,
                CancellationToken _) =>
                data.AsQueryable().Where(predicate).ToList());
        repository.Setup(repo => repo.AddAsync(
                It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repository;
    }
}
