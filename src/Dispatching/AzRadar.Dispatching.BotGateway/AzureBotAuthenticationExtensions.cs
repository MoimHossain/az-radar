using Microsoft.Agents.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AzRadar.Dispatching.BotGateway;

public static class AzureBotAuthenticationExtensions
{
    public static IServiceCollection AddAzureBotAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var audiences = configuration
            .GetSection("TokenValidation:Audiences")
            .Get<string[]>() ?? [];
        if (audiences.Length == 0 || audiences.Any(audience => !Guid.TryParse(audience, out _)))
        {
            throw new InvalidOperationException(
                "TokenValidation:Audiences must contain the Azure Bot managed identity client ID.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress =
                    AuthenticationConstants.PublicAzureBotServiceOpenIdMetadataUrl;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = AuthenticationConstants.BotFrameworkTokenIssuer,
                    ValidateAudience = true,
                    ValidAudiences = audiences,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };
            });
        services.AddAuthorization();
        return services;
    }
}
