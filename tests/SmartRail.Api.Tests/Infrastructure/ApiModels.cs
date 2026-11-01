namespace SmartRail.Api.Tests.Infrastructure;

/// <summary>
/// The response bodies the tests read, written here as plain records so the tests depend on the JSON
/// contract and not on an application type. Only the fields a test asserts on are present; the
/// serializer ignores the rest.
/// </summary>
internal sealed record JourneySummary(
    Guid Id,
    string? ServiceName,
    string? LineCode,
    string Status,
    int? DelayMinutes,
    StationRef? LastDepartedStation,
    StationRef? NextStation,
    string? GuardName,
    DateTimeOffset LastUpdatedAt,
    bool IsStale);

/// <summary>One journey with every stop and every change published on it.</summary>
internal sealed record JourneyDetail(
    JourneySummary Journey,
    IReadOnlyList<JourneyStop> Stops,
    IReadOnlyList<JourneyChange> Changes);

/// <summary>One stop of a journey as the controller's view shows it.</summary>
internal sealed record JourneyStop(
    int Sequence,
    StationRef? Station,
    bool IsCalling,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? ExpectedArrival,
    DateTimeOffset? ExpectedDeparture,
    DateTimeOffset? ActualDeparture,
    PlatformRef? Platform);

/// <summary>One change published on a journey.</summary>
internal sealed record JourneyChange(
    Guid Id,
    string Type,
    IReadOnlyDictionary<string, string?> Values,
    string? PublisherName,
    DateTimeOffset PublishedAt,
    string Provenance,
    bool Corrected);

/// <summary>One of the calling guard's journeys.</summary>
internal sealed record GuardJourney(
    Guid Id,
    string? ServiceName,
    string? LineCode,
    string Status,
    int? DelayMinutes,
    DateTimeOffset LastUpdatedAt,
    IReadOnlyList<GuardStop> Stops);

/// <summary>One journey with the changes published on it, as the guard sees them.</summary>
internal sealed record GuardJourneyDetail(GuardJourney Journey, IReadOnlyList<GuardChange> Changes);

/// <summary>One change published on a journey, as the guard's view shows it: no publisher, no provenance.</summary>
internal sealed record GuardChange(
    Guid Id,
    string Type,
    IReadOnlyDictionary<string, string?> Values,
    DateTimeOffset PublishedAt,
    bool Corrected);

/// <summary>One calling stop as the guard's view shows it.</summary>
internal sealed record GuardStop(
    int Sequence,
    StationRef? Station,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? ExpectedArrival,
    DateTimeOffset? ExpectedDeparture,
    DateTimeOffset? ActualDeparture,
    PlatformRef? Platform,
    DateTimeOffset? PlatformChangedAt,
    bool DepartureLogged,
    bool IsFinalStop);

/// <summary>A station with the platforms a platform change may choose from.</summary>
internal sealed record Station(Guid Id, string Code, string Name, bool IsInterchange, IReadOnlyList<Platform> Platforms);

/// <summary>One platform of a station.</summary>
internal sealed record Platform(Guid Id, Guid StationId, string Number);

/// <summary>A station by identifier and display name.</summary>
internal sealed record StationRef(Guid Id, string Name);

/// <summary>A platform by identifier and the number passengers see.</summary>
internal sealed record PlatformRef(Guid Id, string Number);

/// <summary>The departure log the API answers a sync with.</summary>
internal sealed record DepartureLog(
    Guid Id,
    Guid JourneyId,
    int StopSequence,
    DateTimeOffset DepartedAt,
    DateTimeOffset ReceivedAt,
    string? DelayCause);

/// <summary>The provider's registered callback URL.</summary>
internal sealed record WebhookSubscription(Guid Id, string Url, bool IsActive, DateTimeOffset CreatedAt);
