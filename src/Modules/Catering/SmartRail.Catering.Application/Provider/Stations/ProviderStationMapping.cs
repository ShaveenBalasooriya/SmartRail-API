using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Catering.Application.Provider.Stations;

/// <summary>
/// Builds the provider's station view from the Rail Network catalogue (design D3; provider-data-api
/// spec, "Stations and platforms"). Both station reads use it, so the list and the single station
/// carry the same platforms and lines.
/// </summary>
internal static class ProviderStationMapping
{
    /// <summary>The provider view of one station.</summary>
    public static ProviderStationResponse ToResponse(this StationDto station)
    {
        ArgumentNullException.ThrowIfNull(station);

        return new ProviderStationResponse(
            station.Id,
            station.Code,
            station.Name,
            station.IsInterchange,
            [.. station.Platforms.Select(platform => new ProviderPlatformResponse(platform.Id, platform.Number))],
            [.. station.Lines.Select(line => new ProviderLineResponse(line.Id, line.Name))]);
    }
}
