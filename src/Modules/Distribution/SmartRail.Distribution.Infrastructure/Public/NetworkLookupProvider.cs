using SmartRail.Distribution.Application.Public;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Distribution.Infrastructure.Public;

/// <summary>
/// Loads the <see cref="NetworkLookup"/> from the Rail Network's read contracts: stations and their
/// platforms from the catalogue, lines from the catalogue, and service names from the timetable
/// (design D1). The lookup is read once per scope, so a request with many journeys makes one pass
/// over the network and then only loads services it has not seen yet.
/// </summary>
internal sealed class NetworkLookupProvider(INetworkCatalog network, ITimetable timetable)
    : INetworkLookupProvider
{
    private readonly Dictionary<Guid, ServiceDto?> _services = [];

    private Task<Catalogue>? _catalogue;

    public async Task<NetworkLookup> GetAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);

        var catalogue = await (_catalogue ??= LoadCatalogueAsync(cancellationToken));

        var services = new Dictionary<Guid, ServiceDto>();
        foreach (var serviceId in serviceIds.Distinct())
        {
            var service = await ServiceAsync(serviceId, cancellationToken);
            if (service is not null)
            {
                services[serviceId] = service;
            }
        }

        return new NetworkLookup(catalogue.Stations, catalogue.Platforms, catalogue.Lines, services);
    }

    private async Task<ServiceDto?> ServiceAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        if (!_services.TryGetValue(serviceId, out var service))
        {
            service = await timetable.GetServiceAsync(serviceId, cancellationToken);
            _services[serviceId] = service;
        }

        return service;
    }

    private async Task<Catalogue> LoadCatalogueAsync(CancellationToken cancellationToken)
    {
        var stations = await network.GetStationsAsync(cancellationToken);

        // A line's code and name are the same in both directions; the outbound entry keeps the
        // line's own station order, since inbound is that order reversed (Rail Network contract).
        var outbound = await network.GetLinesAsync(ServiceDirection.Outbound, cancellationToken);
        var inbound = await network.GetLinesAsync(ServiceDirection.Inbound, cancellationToken);

        return new Catalogue(
            stations.ToDictionary(station => station.Id),
            stations
                .SelectMany(station => station.Platforms)
                .ToDictionary(platform => platform.Id),
            outbound
                .Concat(inbound)
                .GroupBy(line => line.Id)
                .ToDictionary(group => group.Key, group => group.First()));
    }

    /// <summary>The part of the lookup that does not depend on which services a request reads.</summary>
    private sealed record Catalogue(
        IReadOnlyDictionary<Guid, StationDto> Stations,
        IReadOnlyDictionary<Guid, PlatformDto> Platforms,
        IReadOnlyDictionary<Guid, LineDto> Lines);
}
