using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.TrainingData;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

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
}
