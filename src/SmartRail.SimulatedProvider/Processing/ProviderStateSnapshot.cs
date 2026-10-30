namespace SmartRail.SimulatedProvider.Processing;

/// <summary>
/// The response of <c>GET /state</c>: everything the kitchen screen shows, and the registration and
/// failure-switch status the demonstration needs (simulated-provider spec, "Kitchen screen").
/// </summary>
/// <param name="Station">The station whose journeys this provider serves.</param>
/// <param name="Registration">The webhook subscription this provider registered.</param>
/// <param name="LoadedAt">When the journeys were last loaded from the provider API.</param>
/// <param name="Failing">Whether the failure switch makes the webhook endpoint answer 503.</param>
/// <param name="Journeys">The journeys calling at the station, in expected-arrival order.</param>
/// <param name="ReceivedEvents">The events received, newest last, with whether each was applied.</param>
public sealed record ProviderStateSnapshot(
    StationSnapshot Station,
    RegistrationSnapshot Registration,
    DateTimeOffset? LoadedAt,
    bool Failing,
    IReadOnlyList<JourneySnapshot> Journeys,
    IReadOnlyList<ReceivedEvent> ReceivedEvents);

/// <summary>A station by identifier and name.</summary>
public sealed record StationSnapshot(Guid Id, string? Name);

/// <summary>The webhook registration the provider made on start-up.</summary>
public sealed record RegistrationSnapshot(
    bool Registered,
    Guid? SubscriptionId,
    string? Url,
    string? Error);

/// <summary>
/// One journey's state at this provider's station: its expected arrival, platform and status, plus
/// the last sequence number applied and the delay currently in force.
/// </summary>
public sealed record JourneySnapshot(
    Guid JourneyId,
    string? Line,
    string? Direction,
    Guid? FinalDestinationId,
    string? FinalDestinationName,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ExpectedArrival,
    string? Platform,
    string Status,
    long LastAppliedSequence,
    int? DelayMinutes);

/// <summary>One received event, with the outcome the provider's processing rules gave it.</summary>
/// <param name="Outcome">One of <c>applied</c>, <c>ignored_duplicate</c>, <c>out_of_order</c> or <c>ignored_unknown_journey</c>.</param>
public sealed record ReceivedEvent(
    Guid EventId,
    Guid JourneyId,
    string Type,
    long SequenceNumber,
    DateTimeOffset ReceivedAt,
    string Outcome);
