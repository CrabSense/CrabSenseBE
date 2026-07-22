using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Options;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: seed kênh thông báo (gồm Zalo) + mark read.</summary>
public class NotificationServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<ILogger<NotificationService>> _logger = new();
    private readonly IOptions<FcmOptions> _fcm = Options.Create(new FcmOptions());

    private NotificationService Create() =>
        new(_uow.Object, _httpClientFactory.Object, _logger.Object, _fcm);

    [Fact]
    public async Task GetChannels_SeedsDefaultsIncludingZalo()
    {
        var channelRepo = new Mock<IRepository<NotificationChannel>>();
        var stored = new List<NotificationChannel>();

        channelRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<NotificationChannel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored.Count > 0);
        channelRepo.Setup(r => r.AddAsync(It.IsAny<NotificationChannel>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationChannel, CancellationToken>((c, _) => stored.Add(c))
            .Returns(Task.CompletedTask);
        channelRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);

        _uow.Setup(u => u.NotificationChannels).Returns(channelRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().GetChannelsAsync();

        result.Success.Should().BeTrue();
        result.Data!.Select(c => c.ChannelCode).Should().Contain(new[] { "in_app", "telegram", "zalo_oa", "push", "email" });
    }

    [Fact]
    public async Task MarkRead_SetsFlags()
    {
        var id = Guid.NewGuid();
        var n = new Notification { Id = id, IsRead = false };
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(n);
        repo.Setup(r => r.Update(It.IsAny<Notification>()));
        _uow.Setup(u => u.Notifications).Returns(repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().MarkReadAsync(id);
        result.Success.Should().BeTrue();
        n.IsRead.Should().BeTrue();
        n.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RegisterPushToken_CreatesNewToken()
    {
        var userId = Guid.NewGuid();
        var repo = new Mock<IRepository<UserPushToken>>();
        var stored = new List<UserPushToken>();

        repo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserPushToken, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<UserPushToken, bool>> pred, CancellationToken _) =>
                stored.AsQueryable().Where(pred).ToList());
        repo.Setup(r => r.AddAsync(It.IsAny<UserPushToken>(), It.IsAny<CancellationToken>()))
            .Callback<UserPushToken, CancellationToken>((t, _) => stored.Add(t))
            .Returns(Task.CompletedTask);

        _uow.Setup(u => u.UserPushTokens).Returns(repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().RegisterPushTokenAsync(
            userId, new RegisterPushTokenRequest("fcm-token-abc", "android", "device-1"));

        result.Success.Should().BeTrue();
        stored.Should().ContainSingle(t => t.Token == "fcm-token-abc" && t.IsActive);
    }
}
