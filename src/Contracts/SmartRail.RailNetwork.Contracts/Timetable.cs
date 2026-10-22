namespace SmartRail.RailNetwork.Contracts;

/// <summary>
/// Read access to the timetable and the journeys generated from it (architecture §3). Other
/// modules use this instead of reading the <c>network</c> schema.
/// </summary>
public interface ITimetable
{
    /// <summary>One service with its stops in sequence order, or <see langword="null"/> when the ID is unknown.</summary>
    Task<ServiceDto?> GetServiceAsync(Guid serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The scheduled journeys for one railway calendar day, in scheduled-departure order, each with
    /// its full stop baseline.
    /// </summary>
    Task<IReadOnlyList<JourneyDto>> GetScheduledJourneysAsync(DateOnly serviceDate, CancellationToken cancellationToken = default);
}

/// <summary>A timetable service and its stops in sequence order.</summary>
public sealed record ServiceDto(
    Guid Id,
    string Name,
    Guid LineId,
    ServiceDirection Direction,
    Guid GuardStaffId,
    IReadOnlyList<ServiceStopDto> Stops);

/// <summary>
/// One stop of a service. Times are local times of day in the railway's time zone; a non-calling
/// stop has no times and no platform.
/// </summary>
public sealed record ServiceStopDto(
    int Sequence,
    Guid StationId,
    bool IsCalling,
    TimeOnly? ScheduledArrival,
    TimeOnly? ScheduledDeparture,
    Guid? PlatformId);

/// <summary>A dated operational journey and its stops in sequence order.</summary>
public sealed record JourneyDto(
    Guid Id,
    Guid ServiceId,
    string ServiceName,
    Guid LineId,
    string LineCode,
    string LineName,
    ServiceDirection Direction,
    DateOnly ServiceDate,
    Guid GuardStaffId,
    DateTimeOffset ScheduledDepartureAt,
    IReadOnlyList<JourneyStopDto> Stops);

/// <summary>
/// One stop of a journey, with its scheduled times as UTC instants. A non-calling stop has no
/// times and no platform.
/// </summary>
public sealed record JourneyStopDto(
    int Sequence,
    Guid StationId,
    bool IsCalling,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    Guid? PlatformId);
