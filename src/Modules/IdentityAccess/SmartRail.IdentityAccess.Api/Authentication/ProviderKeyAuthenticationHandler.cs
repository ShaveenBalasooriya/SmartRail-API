using System.Security.Claims;
using System.Text.Encodings.Web;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SmartRail.IdentityAccess.Application.ProviderKeys.AuthenticateProviderKey;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.IdentityAccess.Api.Authentication;

/// <summary>
/// Authenticates a provider from the <c>X-Api-Key</c> header by sending the key to the Identity
/// application query (design D7). The query returns the provider's ID or one unauthorized error for
/// every failure, so this handler never learns — and never reveals — why a key was refused.
/// </summary>
/// <remarks>
/// The presented key is a credential: it is passed to the query and is never written to a log, an
/// exception message or the authentication failure reason.
/// </remarks>
public sealed class ProviderKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ISender _sender;

    public ProviderKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISender sender)
        : base(options, logger, encoder)
    {
        _sender = sender;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No key is not a failed key: another scheme may still answer, and an endpoint that
        // requires a provider is challenged with 401 either way.
        if (!Request.Headers.TryGetValue(ProviderKeyAuthentication.HeaderName, out StringValues presented)
            || StringValues.IsNullOrEmpty(presented))
        {
            return AuthenticateResult.NoResult();
        }

        var result = await _sender.Send(
            new AuthenticateProviderKeyQuery(presented.ToString()),
            Context.RequestAborted);

        if (result.IsFailure)
        {
            return AuthenticateResult.Fail(ProviderKeyAuthentication.FailureMessage);
        }

        var identity = new ClaimsIdentity(
            [new Claim(SmartRailClaims.ProviderId, result.Value.ToString())],
            ProviderKeyAuthentication.Scheme);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), ProviderKeyAuthentication.Scheme));
    }
}
