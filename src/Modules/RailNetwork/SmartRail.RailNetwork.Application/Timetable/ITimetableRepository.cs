using SmartRail.RailNetwork.Contracts;

namespace SmartRail.RailNetwork.Application.Timetable;

/// <summary>Reads the timetable and its generated journeys from the module's own schema.</summary>
public interface ITimetableRepository
{
    /// <summary>One service with its stops in sequence order, or <see langword="null"/> when unknown.</summary>
    Task<ServiceDto?> GetServiceAsync(Guid serviceId, CancellationToken cancellationToken = default);

    /// <summary>The scheduled journeys of one railway calendar day, in scheduled-departure order.</summary>
    Task<IReadOnlyList<JourneyDto>> GetScheduledJourneysAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);
}
