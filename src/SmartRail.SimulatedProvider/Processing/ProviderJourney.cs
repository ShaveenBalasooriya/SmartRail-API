namespace SmartRail.SimulatedProvider.Processing;

/// <summary>
/// One journey as the provider API returned it on start-up (provider-data-api spec). It is the
/// baseline a correction recomputes from, so it is kept apart from the live state the events change.
/// </summary>
public sealed record ProviderJourney(
    Guid JourneyId,
    string? Line,
    string? Direction,
    Guid? FinalDestinationId,
    string? FinalDestinationName,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ExpectedArrival,
    string? Platform,
    string Status,
    long SequenceNumber);

/// <summary>A station's name, as the provider API returns it, for the kitchen screen's heading.</summary>
public sealed record ProviderStation(Guid Id, string Name);
