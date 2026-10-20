using Microsoft.Extensions.DependencyInjection;

namespace SmartRail.LiveOperations.Api;

/// <summary>
/// The Live Operations API layer's registration point. The Host calls this and then maps every
/// Carter module it finds in this assembly under <c>/api/v1</c>.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers the API layer's services.</summary>
    public static IServiceCollection AddLiveOperationsApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
