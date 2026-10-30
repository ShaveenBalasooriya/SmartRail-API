using System.Text.Json.Serialization;
using SmartRail.Distribution.Application.Public.Guidance;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Distribution.Application.Public;

/// <summary>
/// One journey as every public channel shows it: the departures and connections lists, the journey
/// detail, the station display feed and the stream payloads all return this one shape, so the same
/// data reaches the passenger app and the display by construction (design D1). It holds no staff
/// identifier or name (no-personal-data spec, EP-14).
/// </summary>
/// <param name="JourneyId">The journey's identifier.</param>
/// <param name="ServiceName">The service's name, or <see langword="null"/> when the timetable no longer holds the service.</param>
/// <param name="Line">The line the journey runs on, always with its identifier.</param>
/// <param name="Direction">The direction the service runs in, or <see langword="null"/> when the timetable no longer holds the service.</param>
/// <param name="ServiceDate">The railway calendar day the journey runs on.</param>
/// <param name="Status">The journey's lifecycle status.</param>
/// <param name="DelayMinutes">Minutes early (negative) or late (positive) at the next calling stop, or <see langword="null"/> when the journey carries no delay.</param>
/// <param name="LastDepartedStation">The station of the last recorded departure, if any.</param>
/// <param name="NextStation">The station the journey calls at next, if any.</param>
/// <param name="LastUpdatedAt">When the journey's live state last changed.</param>
/// <param name="CorrectedAt">When a published change on the journey was last corrected, if it was.</param>
/// <param name="Provenance">
/// <see cref="PublicProvenance.AutomaticallyPublished"/> when the journey currently carries an
/// uncorrected published disruption, otherwise <see langword="null"/>.
/// </param>
/// <param name="Guidance">The plain-language guidance for an uncorrected disruption, or <see langword="null"/> when the journey carries none (disruption-guidance spec).</param>
/// <param name="Stops">Every stop in sequence order, calling and non-calling.</param>
public sealed record PublicJourneyView(
    Guid JourneyId,
    string? ServiceName,
    PublicLineRef Line,
    ServiceDirection? Direction,
    DateOnly ServiceDate,
    JourneyLifecycleStatus Status,
    int? DelayMinutes,
    PublicStationRef? LastDepartedStation,
    PublicStationRef? NextStation,
    DateTimeOffset LastUpdatedAt,
    DateTimeOffset? CorrectedAt,
    PublicProvenance? Provenance,
    PublicGuidance? Guidance,
    IReadOnlyList<PublicStopView> Stops)
{
    /// <summary>
    /// The timetable service the journey runs. It is not part of the public payload: a channel that
    /// needs the timetable's own stop (the display feed's moved notices read the platform the
    /// timetable assigned) resolves it here instead of matching a service by name.
    /// </summary>
    [JsonIgnore]
    public Guid ServiceId { get; init; }
}

/// <summary>
/// One stop as the public view shows it. A stop the train passes without calling carries no times
/// and no platform (passenger-departures spec, journey detail).
/// </summary>
/// <param name="Sequence">The stop's place in the journey.</param>
/// <param name="Station">The station the stop is at, always with its identifier.</param>
/// <param name="IsCalling">False for a stop the journey passes without calling; such a stop has no times and no platform.</param>
/// <param name="ScheduledArrival">The timetabled arrival, when the stop is a calling stop with one.</param>
/// <param name="ScheduledDeparture">The timetabled departure, when the stop is a calling stop with one.</param>
/// <param name="ExpectedArrival">The arrival the live state currently expects.</param>
/// <param name="ExpectedDeparture">The departure the live state currently expects.</param>
/// <param name="ActualDeparture">The departure the Guard recorded, when it has been recorded.</param>
/// <param name="Platform">The platform the stop uses now, after any uncorrected platform change.</param>
/// <param name="PlatformChangedAt">When the current platform was published; <see langword="null"/> for the timetabled platform.</param>
/// <param name="Provenance">
/// <see cref="PublicProvenance.AutomaticallyPublished"/> when the stop carries an uncorrected
/// published platform change, otherwise <see langword="null"/>.
/// </param>
public sealed record PublicStopView(
    int Sequence,
    PublicStationRef Station,
    bool IsCalling,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? ExpectedArrival,
    DateTimeOffset? ExpectedDeparture,
    DateTimeOffset? ActualDeparture,
    PublicPlatformRef? Platform,
    DateTimeOffset? PlatformChangedAt,
    PublicProvenance? Provenance);

/// <summary>A station by identifier and display name, or a null name when the catalogue lacks it.</summary>
public sealed record PublicStationRef(Guid Id, string? Name);

/// <summary>A platform by identifier and the number a station's passengers see, or a null number when the catalogue lacks it.</summary>
public sealed record PublicPlatformRef(Guid Id, string? Number);

/// <summary>
/// A line by identifier, code, display colour and display name, or null values when the catalogue
/// lacks it.
/// </summary>
public sealed record PublicLineRef(Guid Id, string? Code, string? Color, string? Name);

/// <summary>
/// How a change reached the channels. MVP 1 has one label: every published change takes effect as
/// soon as the controller publishes it, with no review step (M-16, W-08, W-09).
/// </summary>
public enum PublicProvenance
{
    /// <summary>The change took effect as soon as the controller published it.</summary>
    AutomaticallyPublished,
}
