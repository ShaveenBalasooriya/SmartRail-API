using SmartRail.RailNetwork.Application.Timetable;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.RailNetwork.UnitTests.TestDoubles;

/// <summary>
/// An in-memory timetable repository. It answers with the services and journeys it was given and
/// records the date each scheduled-journey request asked for, so the handler's defaulting rule can be
/// checked without a database.
/// </summary>
internal sealed class FakeTimetableRepository : ITimetableRepository
{
    /// <summary>The services GetServiceAsync can find, by identifier.</summary>
    internal Dictionary<Guid, ServiceDto> ServicesById { get; } = [];

    /// <summary>The journeys GetScheduledJourneysAsync returns, by railway calendar day.</summary>
    internal Dictionary<DateOnly, IReadOnlyList<JourneyDto>> JourneysByDate { get; } = [];

    /// <summary>The date of every GetScheduledJourneysAsync call, in call order.</summary>
    internal List<DateOnly> RequestedDates { get; } = [];

    public Task<ServiceDto?> GetServiceAsync(Guid serviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ServicesById.GetValueOrDefault(serviceId));

    public Task<IReadOnlyList<JourneyDto>> GetScheduledJourneysAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        RequestedDates.Add(serviceDate);

        return Task.FromResult(JourneysByDate.GetValueOrDefault(serviceDate) ?? []);
    }
}
