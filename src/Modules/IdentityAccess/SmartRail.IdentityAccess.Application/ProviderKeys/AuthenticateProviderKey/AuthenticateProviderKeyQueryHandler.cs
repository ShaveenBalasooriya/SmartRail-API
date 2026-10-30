using SmartRail.Catering.Contracts;
using SmartRail.IdentityAccess.Domain.Entities;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.IdentityAccess.Application.ProviderKeys.AuthenticateProviderKey;

internal sealed class AuthenticateProviderKeyQueryHandler(
    IProviderApiKeyRepository keys,
    ICateringProviderDirectory providers)
    : IQueryHandler<AuthenticateProviderKeyQuery, Guid>
{
    // One error for every failure mode: a caller cannot tell an unknown prefix from a wrong secret
    // from a provider that has been deactivated.
    private static readonly Error Unauthorized =
        Error.Unauthorized("provider_key.invalid", "The API key is missing or invalid.");

    public async Task<Result<Guid>> Handle(
        AuthenticateProviderKeyQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prefix = ProviderApiKey.ExtractPrefix(request.PresentedKey);
        if (prefix is null)
        {
            return Result.Failure<Guid>(Unauthorized);
        }

        var key = await keys.GetByPrefixAsync(prefix, cancellationToken);

        if (key is null || key.IsRevoked || !key.Matches(request.PresentedKey))
        {
            return Result.Failure<Guid>(Unauthorized);
        }

        // Identity owns the key; Catering owns the provider record, so it decides whether the
        // provider is still active (identity design D7; provider-data-api spec).
        if (!await providers.IsActiveAsync(key.ProviderId, cancellationToken))
        {
            return Result.Failure<Guid>(Unauthorized);
        }

        return Result.Success(key.ProviderId);
    }
}
