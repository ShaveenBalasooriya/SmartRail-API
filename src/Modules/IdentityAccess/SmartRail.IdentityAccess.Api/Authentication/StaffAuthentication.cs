using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace SmartRail.IdentityAccess.Api.Authentication;

/// <summary>
/// Registers the <c>staff</c> JWT bearer scheme against Authentik's OIDC metadata (design D1).
/// Tokens are short-lived access tokens; their content is never logged.
/// </summary>
public static class StaffAuthentication
{
    /// <summary>The scheme name; the <c>staff</c> policy and the default scheme use it.</summary>
    public const string Scheme = "staff";

    /// <summary>The discovery document the signing keys are read from.</summary>
    public const string MetadataAddressSetting = "Authentication:Staff:MetadataAddress";

    /// <summary>The issuer tokens carry, which differs from the discovery address inside Compose.</summary>
    public const string ValidIssuerSetting = "Authentication:Staff:ValidIssuer";

    /// <summary>The staff client ID, which is the token audience.</summary>
    public const string AudienceSetting = "Authentication:Staff:Audience";

    /// <summary>The claim the provider uses for the staff username, which is the account subject.</summary>
    public const string SubjectClaim = "sub";

    /// <summary>The environment name the end-to-end stack runs in; it may use a plain-http issuer.</summary>
    public const string EndToEndEnvironment = "E2E";

    /// <summary>Adds the staff scheme and makes it the application's default authentication.</summary>
    public static IServiceCollection AddStaffAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var metadataAddress = Required(configuration, MetadataAddressSetting);
        var validIssuer = Required(configuration, ValidIssuerSetting);
        var audience = Required(configuration, AudienceSetting);

        // A plain-http discovery address belongs to the local stack (Development) and the E2E
        // environment. Every other environment must reach the issuer over https.
        var allowsPlainHttp = environment.IsDevelopment()
            || environment.IsEnvironment(EndToEndEnvironment);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = Scheme;
                options.DefaultChallengeScheme = Scheme;
            })
            .AddJwtBearer(Scheme, options =>
            {
                options.MetadataAddress = metadataAddress;

                options.RequireHttpsMetadata =
                    !(allowsPlainHttp && metadataAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

                // The token's claims are read by their JWT names: "sub" is the subject, not nameidentifier.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = validIssuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = SubjectClaim,

                    // The principal's authentication type is the scheme, which is how the claims
                    // transformation recognises a staff principal without inspecting its claims.
                    AuthenticationType = Scheme,
                };
            });

        return services;
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Configuration setting '{key}' is required for staff authentication.")
            : value;
    }
}
