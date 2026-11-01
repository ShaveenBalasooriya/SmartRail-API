using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Catering.UnitTests.TestDoubles;

/// <summary>A timetable that returns only the services supplied by the test.</summary>
internal sealed class StubTimetable(params ServiceDto[] services) : ITimetable
{
    private readonly Dictionary<Guid, ServiceDto> _services = services.ToDictionary(service => service.Id);

    public List<Guid> RequestedServiceIds { get; } = [];

    public Task<ServiceDto?> GetServiceAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        RequestedServiceIds.Add(serviceId);
        return Task.FromResult(_services.GetValueOrDefault(serviceId));
    }

    public Task<IReadOnlyList<JourneyDto>> GetScheduledJourneysAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The station journeys query does not read scheduled journeys.");
}
