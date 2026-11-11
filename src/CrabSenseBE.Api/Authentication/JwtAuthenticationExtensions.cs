using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace CrabSenseBE.Api.Authentication;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddCrabSenseJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is required");
        if (secret.Length < 32
            || secret.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Jwt:Secret must be at least 32 characters and must not be a CHANGE_ME placeholder.");
        }

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = configuration["Jwt:Audience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateDatabaseStateAsync
                };
            });

        return services;
    }

    private static async Task ValidateDatabaseStateAsync(
        TokenValidatedContext context)
    {
        var principal = context.Principal;
        var rawUserId =
            principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var tokenRole = principal?.FindFirstValue(ClaimTypes.Role)
            ?? principal?.FindFirstValue("role");
        var rawTokenVersion = principal?.FindFirstValue("token_version");

        if (!Guid.TryParse(rawUserId, out var userId)
            || string.IsNullOrWhiteSpace(tokenRole)
            || !int.TryParse(rawTokenVersion, out var tokenVersion))
        {
            context.Fail("Access token is missing required security claims.");
            return;
        }

        try
        {
            var unitOfWork = context.HttpContext.RequestServices
                .GetRequiredService<IUnitOfWork>();
            var user = await unitOfWork.Users.GetByIdAsync(
                userId, context.HttpContext.RequestAborted);

            if (user is null
                || !user.IsActive
                || !string.Equals(
                    tokenRole, user.Role.ToString(), StringComparison.Ordinal)
                || tokenVersion != user.TokenVersion)
            {
                context.Fail("Access token has been revoked.");
            }
        }
        catch (OperationCanceledException)
            when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            context.Fail("Access token validation was cancelled.");
        }
        catch
        {
            // Fail closed if the live account state cannot be read.
            context.Fail("Access token state could not be validated.");
        }
    }
}
