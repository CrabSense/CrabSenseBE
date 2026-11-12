using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CrabSenseBE.Api.Authentication;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

public sealed class JwtAuthenticationSecurityTests : IAsyncLifetime
{
    private const string Secret =
        "CrabSense_Test_Jwt_Secret_At_Least_32_Characters_Long!";
    private readonly AppUser _user = new()
    {
        Id = Guid.NewGuid(),
        Username = "manager",
        Email = "manager@example.test",
        Role = UserRole.FarmManager,
        IsActive = true,
        TokenVersion = 0
    };
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private JwtService _jwt = null!;

    public Task InitializeAsync()
    {
        var configuration = Configuration();
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                id == _user.Id ? _user : null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Users).Returns(users.Object);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddSingleton(uow.Object);
        builder.Services.AddCrabSenseJwtAuthentication(configuration);
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapGet("/protected", () => Results.Ok())
            .RequireAuthorization();
        _app.StartAsync().GetAwaiter().GetResult();
        _client = _app.GetTestServer().CreateClient();
        _jwt = new JwtService(configuration);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Database_state_revokes_old_access_tokens()
    {
        var original = _jwt.GenerateAccessToken(_user).Token;
        (await Send(original)).ShouldBe(HttpStatusCode.OK);
        (await Send(null)).ShouldBe(HttpStatusCode.Unauthorized);
        var tokenParts = original.Split('.');
        tokenParts[2] = (tokenParts[2][0] == 'a' ? 'b' : 'a')
            + tokenParts[2][1..];
        (await Send(string.Join('.', tokenParts)))
            .ShouldBe(HttpStatusCode.Unauthorized);

        _user.IsActive = false;
        (await Send(original)).ShouldBe(HttpStatusCode.Unauthorized);

        _user.IsActive = true;
        _user.Role = UserRole.FarmStaff;
        (await Send(original)).ShouldBe(HttpStatusCode.Unauthorized);

        _user.Role = UserRole.FarmManager;
        _user.TokenVersion++;
        (await Send(original)).ShouldBe(HttpStatusCode.Unauthorized);

        var deletedUserToken = _jwt.GenerateAccessToken(new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "deleted",
            Email = "deleted@example.test",
            Role = UserRole.FarmStaff,
            IsActive = true
        }).Token;
        (await Send(deletedUserToken)).ShouldBe(HttpStatusCode.Unauthorized);

        var afterUnlock = _jwt.GenerateAccessToken(_user).Token;
        (await Send(afterUnlock)).ShouldBe(HttpStatusCode.OK);

        _user.TokenVersion++;
        (await Send(afterUnlock)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task All_current_roles_are_accepted_when_database_state_matches()
    {
        foreach (var role in Enum.GetValues<UserRole>())
        {
            _user.Role = role;
            _user.TokenVersion++;
            (await Send(_jwt.GenerateAccessToken(_user).Token))
                .ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Legacy_token_without_token_version_is_rejected()
    {
        var legacy = JwtTestTokenFactory.Create(
            Secret, _user, includeTokenVersion: false);

        (await Send(legacy)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpStatusCode> Send(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        if (token is not null)
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = Secret,
                ["Jwt:Issuer"] = "CrabSenseBE.Tests",
                ["Jwt:Audience"] = "CrabSenseBE.TestClients",
                ["Jwt:ExpiryMinutes"] = "30"
            })
            .Build();
}

internal static class HttpStatusCodeAssertions
{
    public static void ShouldBe(this HttpStatusCode actual, HttpStatusCode expected) =>
        Assert.True(
            actual == expected,
            $"Expected HTTP {(int)expected}, received {(int)actual}.");
}

internal static class JwtTestTokenFactory
{
    public static string Create(
        string secret,
        AppUser user,
        bool includeTokenVersion)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        if (includeTokenVersion)
        {
            claims.Add(new Claim(
                "token_version",
                user.TokenVersion.ToString(),
                ClaimValueTypes.Integer32));
        }

        var token = new JwtSecurityToken(
            issuer: "CrabSenseBE.Tests",
            audience: "CrabSenseBE.TestClients",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
