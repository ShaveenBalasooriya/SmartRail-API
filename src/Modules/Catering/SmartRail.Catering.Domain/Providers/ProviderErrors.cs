using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Domain.Providers;

/// <summary>The expected failures of creating a <see cref="Provider"/>.</summary>
public static class ProviderErrors
{
    /// <summary>A provider cannot be created without its identifier.</summary>
    public static readonly Error IdRequired =
        Error.Validation("provider.id", "A provider identifier is required.");

    /// <summary>A provider cannot be created without a name.</summary>
    public static readonly Error NameRequired =
        Error.Validation("provider.name", "A provider name is required.");
}
