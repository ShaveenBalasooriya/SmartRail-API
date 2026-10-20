using Microsoft.Extensions.DependencyInjection;

namespace SmartRail.Catering.Api;

/// <summary>
/// The Catering API layer's registration point. The Host calls this and then maps every Carter
/// module it finds in this assembly under <c>/api/v1</c>.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers the API layer's services.</summary>
    public static IServiceCollection AddCateringApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
