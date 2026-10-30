namespace SmartRail.Distribution.Application.Public;

/// <summary>
/// The shape every public live response carries around its journey data, so a client knows when the
/// response was generated and how soon to ask again (passenger-departures spec, freshness; display
/// feed spec, staleness handling).
/// </summary>
/// <param name="GeneratedAt">When the response was generated, from the application's clock.</param>
/// <param name="RefreshAfterSeconds">The interval the client is recommended to wait before reading again.</param>
/// <param name="Data">The payload: a list of journeys, or one journey, depending on the endpoint.</param>
public sealed record PublicEnvelope<T>(
    DateTimeOffset GeneratedAt,
    int RefreshAfterSeconds,
    T Data);
