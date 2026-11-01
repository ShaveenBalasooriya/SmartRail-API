using SmartRail.RailNetwork.Contracts;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>Returns scheduled journeys by railway calendar day.</summary>
internal sealed class StubTimetable : ITimetable
{
    private readonly Dictionary<DateOnly, IReadOnlyList<JourneyDto>> _journeys = new();

    public List<DateOnly> RequestedDates { get; } = [];

    public Exception? ExceptionToThrow { get; set; }

    public void SetJourneys(DateOnly serviceDate, params JourneyDto[] journeys) =>
        _journeys[serviceDate] = journeys;

    public Task<IReadOnlyList<JourneyDto>> GetScheduledJourneysAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        RequestedDates.Add(serviceDate);
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(_journeys.GetValueOrDefault(serviceDate) ?? []);
    }

    public Task<ServiceDto?> GetServiceAsync(Guid serviceId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("These handlers read dated journeys, not services.");
}
