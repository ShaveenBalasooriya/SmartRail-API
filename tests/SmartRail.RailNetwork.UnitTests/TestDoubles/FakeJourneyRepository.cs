using SmartRail.RailNetwork.Application.Journeys;
using SmartRail.RailNetwork.Domain.Entities;

namespace SmartRail.RailNetwork.UnitTests.TestDoubles;

/// <summary>
/// Stands in for the EF Core journey repository. Like the real one, it treats the service and date as
/// the natural key: TryAddAsync reports false when a journey already exists for that pair, and an
/// uncommitted insert is undone when the transaction rolls back.
/// </summary>
internal sealed class FakeJourneyRepository : IJourneyRepository
{
    private readonly FakeNetworkUnitOfWork _unitOfWork;
    private readonly IReadOnlyList<Service> _services;
    private readonly List<Journey> _journeys = [];

    internal FakeJourneyRepository(FakeNetworkUnitOfWork unitOfWork, params Service[] services)
    {
        _unitOfWork = unitOfWork;
        _services = services;
    }

    /// <summary>The rows the repository currently holds, in the order they were created.</summary>
    internal IReadOnlyList<Journey> Stored => _journeys;

    /// <summary>Puts a journey in the table before the handler runs, as a previous generation did.</summary>
    internal void Seed(Journey journey) => _journeys.Add(journey);

    public Task<IReadOnlyList<Service>> GetServicesWithStopsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_services);

    public Task<bool> TryAddAsync(Journey journey, CancellationToken cancellationToken = default)
    {
        var alreadyExists = _journeys.Exists(
            existing => existing.ServiceId == journey.ServiceId && existing.ServiceDate == journey.ServiceDate);

        if (alreadyExists)
        {
            return Task.FromResult(false);
        }

        _journeys.Add(journey);
        _unitOfWork.RegisterRollback(() => _journeys.Remove(journey));

        return Task.FromResult(true);
    }
}
