using System.IdentityModel.Tokens.Jwt;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class JwtServiceTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    public void Access_token_expiry_result_matches_jwt_exp_and_configuration(
        int expiryMinutes)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] =
                    "CrabSense_Test_Secret_With_More_Than_32_Characters!",
                ["Jwt:Issuer"] = "issuer",
                ["Jwt:Audience"] = "audience",
                ["Jwt:ExpiryMinutes"] = expiryMinutes.ToString()
            })
            .Build();
        var service = new JwtService(configuration);
        var issuedAt = DateTime.UtcNow;

        var result = service.GenerateAccessToken(new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "staff",
            Email = "staff@example.test",
            Role = UserRole.FarmStaff,
            TokenVersion = 7
        });

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        token.Claims.Single(claim => claim.Type == "token_version")
            .Value.Should().Be("7");
        result.ExpiresAt.Should().Be(token.ValidTo);
        token.ValidTo.Should().BeCloseTo(
            issuedAt.AddMinutes(expiryMinutes), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Invalid_access_token_lifetime_configuration_is_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] =
                    "CrabSense_Test_Secret_With_More_Than_32_Characters!",
                ["Jwt:ExpiryMinutes"] = "43200"
            })
            .Build();

        var act = () => new JwtService(configuration);

        act.Should().Throw<InvalidOperationException>();
    }
}
