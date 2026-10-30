namespace SmartRail.Distribution.Application.Public;

/// <summary>
/// Supplies the <see cref="NetworkLookup"/> the public view is built from. It is a port because the
/// catalogue lives behind Rail Network's contract, which only the Infrastructure layer loads; a
/// request or scope reads it once and reuses it for every view it builds.
/// </summary>
public interface INetworkLookupProvider
{
    /// <summary>
    /// The catalogue plus the names of <paramref name="serviceIds"/>. A service the timetable does
    /// not hold is left out rather than failing the lookup, and the catalogue is read once per scope.
    /// </summary>
    Task<NetworkLookup> GetAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default);
}
