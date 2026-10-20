using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Options;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
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
        var userId = Guid.NewGuid();
        var n = new Notification { Id = id, UserId = userId, IsRead = false };
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(n);
        repo.Setup(r => r.Update(It.IsAny<Notification>()));
        _uow.Setup(u => u.Notifications).Returns(repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().MarkReadAsync(id, userId);
        result.Success.Should().BeTrue();
        n.IsRead.Should().BeTrue();
        n.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkRead_OfAnotherUser_IsRejected()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var n = new Notification { Id = id, UserId = owner, IsRead = false };
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(n);
        _uow.Setup(u => u.Notifications).Returns(repo.Object);

        var act = () => Create().MarkReadAsync(id, Guid.NewGuid());

        await act.Should().ThrowAsync<CrabSenseBE.Application.Common.AppException>();
        n.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task MarkRead_OfAnotherUser_AllowedForSystemAdmin()
    {
        var id = Guid.NewGuid();
        var n = new Notification { Id = id, UserId = Guid.NewGuid(), IsRead = false };
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(n);
        repo.Setup(r => r.Update(It.IsAny<Notification>()));
        _uow.Setup(u => u.Notifications).Returns(repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().MarkReadAsync(id, Guid.NewGuid(), isSystemAdmin: true);

        result.Success.Should().BeTrue();
        n.IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task GetUnreadCount_CountsOnlyUnread()
    {
        var userId = Guid.NewGuid();
        var all = new List<Notification>
        {
            new() { Id = Guid.NewGuid(), UserId = userId, IsRead = false },
            new() { Id = Guid.NewGuid(), UserId = userId, IsRead = true },
            new() { Id = Guid.NewGuid(), UserId = userId, IsRead = false },
        };
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.CountAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Notification, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<Notification, bool>> pred, CancellationToken _) =>
                all.AsQueryable().Count(pred));

        _uow.Setup(u => u.Notifications).Returns(repo.Object);

        var result = await Create().GetUnreadCountAsync(userId);

        result.Success.Should().BeTrue();
        result.Data!.Count.Should().Be(2);
    }

    [Fact]
    public async Task MarkAllRead_UpdatesEveryUnreadRow()
    {
        var userId = Guid.NewGuid();
        var all = new List<Notification>
        {
            new() { Id = Guid.NewGuid(), UserId = userId, IsRead = false },
            new() { Id = Guid.NewGuid(), UserId = userId, IsRead = true, ReadAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), UserId = userId, IsRead = false },
        };
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Notification, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<Notification, bool>> pred, CancellationToken _) =>
                all.AsQueryable().Where(pred).ToList());
        repo.Setup(r => r.Update(It.IsAny<Notification>()));

        _uow.Setup(u => u.Notifications).Returns(repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().MarkAllReadAsync(userId);

        result.Success.Should().BeTrue();
        all.Should().OnlyContain(n => n.IsRead && n.ReadAt != null);
    }

    private sealed class Harness
    {
        public List<NotificationRule> Rules { get; } = new();
        public List<AppUser> Users { get; } = new();
        public List<NotificationChannel> Channels { get; } = new();
        public List<Notification> Created { get; } = new();
        public List<NotificationDelivery> Deliveries { get; } = new();
    }

    private NotificationService WithRule(Harness h, NotificationRule rule, params AppUser[] users)
    {
        h.Rules.Add(rule);
        h.Users.AddRange(users);
        h.Channels.Add(new NotificationChannel { ChannelCode = "in_app", DisplayName = "In-App", IsEnabled = true });

        var ruleRepo = new Mock<IRepository<NotificationRule>>();
        ruleRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<NotificationRule, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        ruleRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<NotificationRule, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<NotificationRule, bool>> p, CancellationToken _) =>
                h.Rules.AsQueryable().Where(p).ToList());

        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<AppUser, bool>> p, CancellationToken _) =>
                h.Users.AsQueryable().Where(p).ToList());

        var channelRepo = new Mock<IRepository<NotificationChannel>>();
        channelRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<NotificationChannel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        channelRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<NotificationChannel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<NotificationChannel, bool>> p, CancellationToken _) =>
                h.Channels.AsQueryable().Where(p).ToList());

        var notificationRepo = new Mock<IRepository<Notification>>();
        notificationRepo.Setup(r => r.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, _) => h.Created.Add(n))
            .Returns(Task.CompletedTask);

        var deliveryRepo = new Mock<IRepository<NotificationDelivery>>();
        deliveryRepo.Setup(r => r.AddAsync(It.IsAny<NotificationDelivery>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationDelivery, CancellationToken>((d, _) => h.Deliveries.Add(d))
            .Returns(Task.CompletedTask);

        _uow.Setup(u => u.NotificationRules).Returns(ruleRepo.Object);
        _uow.Setup(u => u.Users).Returns(userRepo.Object);
        _uow.Setup(u => u.NotificationChannels).Returns(channelRepo.Object);
        _uow.Setup(u => u.Notifications).Returns(notificationRepo.Object);
        _uow.Setup(u => u.NotificationDeliveries).Returns(deliveryRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return Create();
    }

    [Fact]
    public async Task Notify_WhenRuleDisabled_SendsNothing()
    {
        var h = new Harness();
        var service = WithRule(h, new NotificationRule
        {
            TypeCode = NotificationTypeCodes.KioskDisconnect,
            IsEnabled = false,
            RecipientRoles = "FarmAdministrator",
            ChannelCodes = "in_app",
            TitleTemplate = "Kiosk mất kết nối",
            BodyTemplate = "{message}"
        }, new AppUser { Id = Guid.NewGuid(), Role = UserRole.FarmAdministrator, IsActive = true });

        await service.NotifyAsync(NotificationTypeCodes.KioskDisconnect, "Kiosk mất kết nối: K1", severity: "Critical");

        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Notify_RendersTemplateAndOnlyMatchingRoles()
    {
        var h = new Harness();
        var admin = new AppUser { Id = Guid.NewGuid(), Role = UserRole.FarmAdministrator, IsActive = true };
        var staff = new AppUser { Id = Guid.NewGuid(), Role = UserRole.FarmStaff, IsActive = true };
        var service = WithRule(h, new NotificationRule
        {
            TypeCode = NotificationTypeCodes.SensorThreshold,
            IsEnabled = true,
            RecipientRoles = "FarmAdministrator",
            ChannelCodes = "in_app",
            TitleTemplate = "{sensor} vượt ngưỡng",
            BodyTemplate = "{sensor} = {value}{unit} (ngưỡng {min}–{max})"
        }, admin, staff);

        await service.NotifyAsync(
            NotificationTypeCodes.SensorThreshold, "pH cao: 9 > 8.5", severity: "Warning",
            tokens: new Dictionary<string, string?>
            {
                ["sensor"] = "pH", ["value"] = "9", ["unit"] = "", ["min"] = "7", ["max"] = "8.5"
            });

        h.Created.Should().ContainSingle();
        h.Created[0].UserId.Should().Be(admin.Id);
        h.Created[0].TypeCode.Should().Be(NotificationTypeCodes.SensorThreshold);
        h.Created[0].Title.Should().Be("pH vượt ngưỡng");
        h.Created[0].Body.Should().Be("pH = 9 (ngưỡng 7–8.5)");
    }

    [Fact]
    public async Task Notify_WhenUserDisabledWarnings_SkipsNonCritical()
    {
        var h = new Harness();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Role = UserRole.FarmAdministrator,
            IsActive = true,
            NotificationPrefsJson = """{"warningsEnabled":false,"systemUpdatesEnabled":true}"""
        };
        var service = WithRule(h, new NotificationRule
        {
            TypeCode = NotificationTypeCodes.SensorThreshold,
            IsEnabled = true,
            RecipientRoles = "FarmAdministrator",
            ChannelCodes = "in_app",
            TitleTemplate = "Vượt ngưỡng",
            BodyTemplate = "{message}"
        }, user);

        await service.NotifyAsync(NotificationTypeCodes.SensorThreshold, "pH cao", severity: "Warning");
        h.Created.Should().BeEmpty();

        await service.NotifyAsync(NotificationTypeCodes.SensorThreshold, "pH cao", severity: "Critical");
        h.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task Preview_RendersTokensWithoutSending()
    {
        var h = new Harness();
        var service = WithRule(h, new NotificationRule
        {
            TypeCode = NotificationTypeCodes.RasRelayOn,
            IsEnabled = true,
            RecipientRoles = "FarmAdministrator",
            ChannelCodes = "in_app",
            TitleTemplate = "{name} đã bật",
            BodyTemplate = "{message}"
        });

        var result = await service.PreviewRuleAsync(
            NotificationTypeCodes.RasRelayOn,
            new Dictionary<string, string?> { ["name"] = "Máy bơm 1", ["message"] = "Máy bơm 1 đã bật" });

        result.Data!.Title.Should().Be("Máy bơm 1 đã bật");
        result.Data.Body.Should().Be("Máy bơm 1 đã bật");
        h.Created.Should().BeEmpty();
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
