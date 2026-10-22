using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using SmartRail.IdentityAccess.Api.Authentication;

namespace SmartRail.Host.Http;

/// <summary>
/// Publishes the OpenAPI document and the Scalar reference, in the environments where they are
/// wanted and nowhere else (api-documentation spec, design D5a).
/// </summary>
public static class OpenApiConfiguration
{
    /// <summary>The document name; it appears in the route as <c>/openapi/v1.json</c>.</summary>
    public const string DocumentName = "v1";

    /// <summary>The route the document is served from.</summary>
    public const string DocumentRoute = "/openapi/{documentName}.json";

    /// <summary>The route the Scalar reference is served from.</summary>
    public const string ScalarRoute = "/scalar";

    /// <summary>The configuration key that can force documentation on or off in any environment.</summary>
    public const string EnabledSetting = "Documentation:Enabled";

    /// <summary>The environment name the end-to-end stack runs in.</summary>
    public const string EndToEndEnvironment = "E2E";

    /// <summary>
    /// Documentation is on in Development and E2E. Any other environment needs
    /// <c>Documentation:Enabled=true</c> explicitly, so a production deployment does not publish
    /// its API surface by accident.
    /// </summary>
    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration.GetValue<bool?>(EnabledSetting);
        if (configured.HasValue)
        {
            return configured.Value;
        }

        return environment.IsDevelopment()
            || string.Equals(environment.EnvironmentName, EndToEndEnvironment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Registers the OpenAPI document generator and its SmartRail transformers. The staff OAuth2
    /// URLs are required only when the documentation is served, so a deployment that turns the
    /// reference off does not have to configure them.
    /// </summary>
    public static IServiceCollection AddSmartRailOpenApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var documented = IsEnabled(environment, configuration);
        var staffAuthorizationUrl = documented
            ? Required(configuration, OpenApiSecurity.StaffAuthorizationUrlSetting)
            : null;
        var staffTokenUrl = documented
            ? Required(configuration, OpenApiSecurity.StaffTokenUrlSetting)
            : null;

        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "SmartRail API",
                    Version = "v1",
                    Description =
                        "The SmartRail operations and passenger-information API. Every application endpoint "
                        + "is served under /api/v1. Health probes are served under /health.",
                };
                return Task.CompletedTask;
            });

            options.AddDocumentTransformer(
                new SecuritySchemeDocumentTransformer(staffAuthorizationUrl, staffTokenUrl));
            options.AddOperationTransformer(new EndpointSecurityOperationTransformer());
            options.AddOperationTransformer(new ProblemDetailsOperationTransformer());
        });

        return services;
    }

    /// <summary>Maps the document and the Scalar reference when documentation is enabled.</summary>
    public static WebApplication MapSmartRailDocumentation(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!IsEnabled(app.Environment, app.Configuration))
        {
            return app;
        }

        app.MapOpenApi(DocumentRoute).AllowAnonymous();

        // Scalar serves the reference at "/scalar/" and redirects "/scalar" there, which is how a
        // developer opens it from the route the spec names (api-documentation spec).
        // For the staff scheme this is a public client, so only the client ID, PKCE and the scopes
        // are set: Scalar performs the authorization-code flow and sends the resulting token.
        // The client ID is the same value as the token audience (design D1, D10).
        var staffClientId = Required(app.Configuration, StaffAuthentication.AudienceSetting);
        app.MapScalarApiReference(ScalarRoute, options => options
            .WithTitle("SmartRail API")
            .WithOpenApiRoutePattern("/openapi/v1.json")
            .AddPreferredSecuritySchemes([OpenApiSecurity.StaffScheme, OpenApiSecurity.ProviderScheme])
            .AddAuthorizationCodeFlow(OpenApiSecurity.StaffScheme, flow =>
            {
                flow.ClientId = staffClientId;
                flow.Pkce = Pkce.Sha256;
                flow.SelectedScopes = OpenApiSecurity.StaffScopes;

                // A relative redirect URI is resolved by Scalar against the page it is served from,
                // so it is always the Scalar page itself: "<origin>/scalar/" (for the local stack,
                // http://localhost:5080/scalar/). The Authentik client registers exactly that URI.
                flow.RedirectUri = $"{ScalarRoute}/";
            }))
            .AllowAnonymous();

        return app;
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Configuration setting '{key}' is required when the API documentation is enabled.")
            : value;
    }
}

/// <summary>
/// Documents the error contract every endpoint shares. An endpoint that validates input gets a
/// 400; every endpoint can fail with a 500. Both are problem details, so clients can rely on one
/// error shape (api-documentation spec, api-conventions spec).
/// </summary>
internal sealed class ProblemDetailsOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var responses = operation.Responses ??= new OpenApiResponses();

        // Every endpoint under /api/v1 can reject invalid input, so every operation documents the
        // shared 400 problem-details response unless it already declares one.
        if (!responses.ContainsKey("400"))
        {
            responses["400"] = ProblemResponse("The request is invalid.");
        }

        if (!responses.ContainsKey("500"))
        {
            responses["500"] = ProblemResponse("An unexpected error occurred.");
        }

        return Task.CompletedTask;
    }

    private static OpenApiResponse ProblemResponse(string description) => new()
    {
        Description = description,
        Content = new Dictionary<string, OpenApiMediaType>
        {
            ["application/problem+json"] = new OpenApiMediaType(),
        },
    };
}
