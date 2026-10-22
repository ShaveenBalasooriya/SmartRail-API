using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SmartRail.SharedKernel.Api.Authorization;

namespace SmartRail.Host.Http;

/// <summary>
/// The security schemes the document declares and the names operations reference (api-explorer-
/// authentication spec). The scheme names match the authentication policy names, so the document
/// shows the credential the endpoint's policy actually accepts.
/// </summary>
public static class OpenApiSecurity
{
    /// <summary>The OAuth2 scheme that signs a staff member in and carries the resulting token.</summary>
    public const string StaffScheme = "staff";

    /// <summary>The API-key scheme that carries a provider key in <c>X-Api-Key</c>.</summary>
    public const string ProviderScheme = "provider";

    /// <summary>The public Authentik authorization endpoint the staff scheme points at.</summary>
    public const string StaffAuthorizationUrlSetting = "Authentication:Staff:AuthorizationUrl";

    /// <summary>The public Authentik token endpoint the staff scheme points at.</summary>
    public const string StaffTokenUrlSetting = "Authentication:Staff:TokenUrl";

    /// <summary>
    /// The scopes the staff client requests: identity, the profile (which carries <c>groups</c>) and
    /// a refresh token (design D10).
    /// </summary>
    public static readonly string[] StaffScopes = ["openid", "profile", "offline_access"];
}

/// <summary>
/// Declares the staff OAuth2 authorization-code scheme and the provider API-key scheme once, in the
/// document's components. Staff sign in through Authentik with the same public client the frontends
/// use (design D10); neither scheme carries a credential value, because the application never
/// pre-fills a token or a key.
/// </summary>
internal sealed class SecuritySchemeDocumentTransformer : IOpenApiDocumentTransformer
{
    private readonly Uri? _staffAuthorizationUrl;
    private readonly Uri? _staffTokenUrl;

    public SecuritySchemeDocumentTransformer(string? staffAuthorizationUrl, string? staffTokenUrl)
    {
        _staffAuthorizationUrl = ToUri(staffAuthorizationUrl);
        _staffTokenUrl = ToUri(staffTokenUrl);
    }

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[OpenApiSecurity.StaffScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description =
                "Signs a staff member in through Authentik with the public staff client "
                + "(authorization code with PKCE). The token's groups claim decides the caller's role.",
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = _staffAuthorizationUrl,
                    TokenUrl = _staffTokenUrl,
                    RefreshUrl = _staffTokenUrl,
                    Scopes = OpenApiSecurity.StaffScopes.ToDictionary(
                        scope => scope,
                        scope => scope switch
                        {
                            "openid" => "Authenticate the staff member with OpenID Connect.",
                            "profile" => "Read the profile, which carries the staff member's groups claim.",
                            "offline_access" => "Issue a refresh token so the session can be extended.",
                            _ => scope,
                        }),
                },
            },
        };

        document.Components.SecuritySchemes[OpenApiSecurity.ProviderScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Api-Key",
            Description = "The catering provider's API key, sent in the X-Api-Key header.",
        };

        return Task.CompletedTask;
    }

    private static Uri? ToUri(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new Uri(value, UriKind.Absolute);
}

/// <summary>
/// Gives each operation the requirement its access-policy metadata declares (design D4, D10): a
/// staff endpoint requires the OAuth2 scheme with the staff scopes, a provider endpoint the API-key
/// scheme, and a public endpoint an explicit empty requirement. An endpoint with no classification
/// is closed by the fallback policy, so the document describes it as staff rather than as open.
/// </summary>
internal sealed class EndpointSecurityOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.Description.ActionDescriptor?.EndpointMetadata ?? [];

        if (metadata.OfType<ProviderAccessMetadata>().Any())
        {
            operation.Security = [Requirement(OpenApiSecurity.ProviderScheme, context.Document, [])];
        }
        else if (metadata.OfType<StaffAccessMetadata>().Any())
        {
            operation.Security =
            [
                Requirement(OpenApiSecurity.StaffScheme, context.Document, OpenApiSecurity.StaffScopes),
            ];
        }
        else if (metadata.OfType<PublicAccessMetadata>().Any() || metadata.OfType<IAllowAnonymous>().Any())
        {
            // An explicit empty array overrides any document-level requirement, so a public
            // operation can never inherit one.
            operation.Security = [];
        }
        else
        {
            operation.Security =
            [
                Requirement(OpenApiSecurity.StaffScheme, context.Document, OpenApiSecurity.StaffScopes),
            ];
        }

        return Task.CompletedTask;
    }

    private static OpenApiSecurityRequirement Requirement(
        string scheme,
        OpenApiDocument? document,
        IEnumerable<string> scopes) => new()
    {
        [new OpenApiSecuritySchemeReference(scheme, document)] = [.. scopes],
    };
}
