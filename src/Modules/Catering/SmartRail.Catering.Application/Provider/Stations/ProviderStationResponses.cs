namespace SmartRail.Catering.Application.Provider.Stations;

/// <summary>
/// One station as the provider reads it: its identity, whether it is the interchange, and the
/// platforms and lines a catering provider needs to prepare for a train (provider-data-api spec,
/// "Stations and platforms"). The view is Catering's own, so a Rail Network contract type is never
/// serialised directly.
/// </summary>
/// <param name="Id">The station's identifier.</param>
/// <param name="Code">The station's short code.</param>
/// <param name="Name">The station's display name.</param>
/// <param name="IsInterchange">True when two or more lines serve the station.</param>
/// <param name="Platforms">The station's platforms, in the order the network holds them.</param>
/// <param name="Lines">The lines that serve the station.</param>
public sealed record ProviderStationResponse(
    Guid Id,
    string Code,
    string Name,
    bool IsInterchange,
    IReadOnlyList<ProviderPlatformResponse> Platforms,
    IReadOnlyList<ProviderLineResponse> Lines);

/// <summary>One platform of a station, named by the number its passengers see.</summary>
/// <param name="Id">The platform's identifier.</param>
/// <param name="Number">The platform number, unique within its station.</param>
public sealed record ProviderPlatformResponse(Guid Id, string Number);

/// <summary>One line a station belongs to, or the line a journey runs on.</summary>
/// <param name="Id">The line's identifier.</param>
/// <param name="Name">The line's display name, or <see langword="null"/> when the catalogue no longer holds it.</param>
public sealed record ProviderLineResponse(Guid Id, string? Name);
