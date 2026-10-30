namespace SmartRail.Catering.Application.Webhooks;

/// <summary>
/// The versioned JSON a provider receives for one pushed journey event (design D5). It carries the
/// event's identity, the journey's sequence number, the type, the journey and stop it concerns, the
/// new values, when it was published, the provenance label and, for a correction, the event it
/// withdraws.
/// </summary>
/// <param name="Version">The payload contract's version; one in MVP 1.</param>
/// <param name="EventId">The journey event's identifier, which is also the bus message's identifier.</param>
/// <param name="SequenceNumber">The journey's per-journey sequence number, which may skip numbers for events that are not pushed.</param>
/// <param name="Type">The snake-case pushed event type.</param>
/// <param name="JourneyId">The journey the change belongs to.</param>
/// <param name="StopSequence">The stop the change names, or <see langword="null"/> when it names the whole journey.</param>
/// <param name="StationId">The station of <paramref name="StopSequence"/>, or <see langword="null"/>.</param>
/// <param name="Values">The new values the type carries; empty for cancellation, termination and correction.</param>
/// <param name="PublishedAt">When the change was published.</param>
/// <param name="Provenance">The provenance label, which is <c>automatically_published</c> in MVP 1.</param>
/// <param name="CorrectsEventId">For a correction, the event it withdraws; otherwise <see langword="null"/>.</param>
public sealed record ProviderWebhookPayload(
    int Version,
    Guid EventId,
    long SequenceNumber,
    string Type,
    Guid JourneyId,
    int? StopSequence,
    Guid? StationId,
    IReadOnlyDictionary<string, object?> Values,
    DateTimeOffset PublishedAt,
    string Provenance,
    Guid? CorrectsEventId);
