using SmartRail.SharedKernel.Results;

namespace SmartRail.IdentityAccess.Domain.Entities;

/// <summary>The expected failures of issuing a <see cref="ProviderApiKey"/>.</summary>
public static class ProviderApiKeyErrors
{
    public static readonly Error ProviderIdRequired =
        Error.Validation("provider_api_key.provider_id", "A provider identifier is required.");

    public static readonly Error InvalidFormat = Error.Validation(
        "provider_api_key.format",
        "An API key must have the form srk_<8-character prefix>_<secret>.");
}
