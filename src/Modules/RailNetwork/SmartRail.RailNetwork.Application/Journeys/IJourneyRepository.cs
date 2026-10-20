using SmartRail.RailNetwork.Domain.Entities;

namespace SmartRail.RailNetwork.Application.Journeys;

/// <summary>Reads the services to generate journeys for and inserts the journeys idempotently.</summary>
public interface IJourneyRepository
{
    /// <summary>Every service with its stops loaded, so a journey can be built from the first one.</summary>
    Task<IReadOnlyList<Service>> GetServicesWithStopsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a journey unless one already exists for its service and date. Returns
    /// <see langword="true"/> only when the row was actually created.
    /// </summary>
    Task<bool> TryAddAsync(Journey journey, CancellationToken cancellationToken = default);
}
