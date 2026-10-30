using SmartRail.Catering.Application.Provider.Stations;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Catering.Application.Provider.Journeys;

/// <summary>
/// One journey calling at a station, as the provider reads it: what it is, where it ends, when it is
/// expected at that station, the platform it uses there and how fresh the answer is
/// (provider-data-api spec, "Journeys calling at a station"). The view is Catering's own and carries
/// no staff identifier, guard assignment, audit or passenger data (spec, "Railway data only").
/// </summary>
/// <param name="JourneyId">The journey's identifier.</param>
/// <param name="Line">The line the journey runs on.</param>
/// <param name="Direction">The direction the service runs in, or <see langword="null"/> when the timetable no longer holds it.</param>
/// <param name="FinalDestination">The station of the journey's last calling stop.</param>
/// <param name="ScheduledArrival">The timetabled arrival at the requested station, when it has one.</param>
/// <param name="ExpectedArrival">The arrival the live state currently expects at the requested station.</param>
/// <param name="Platform">The platform the journey uses at the requested station, when it has one.</param>
/// <param name="Status">The journey's lifecycle status.</param>
/// <param name="SequenceNumber">The sequence number of the latest applied event, zero before any event.</param>
/// <param name="LastUpdatedAt">When the journey's live state last changed.</param>
public sealed record ProviderJourneyResponse(
    Guid JourneyId,
    ProviderLineResponse Line,
    ServiceDirection? Direction,
    ProviderStationRefResponse FinalDestination,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ExpectedArrival,
    ProviderPlatformResponse? Platform,
    JourneyLifecycleStatus Status,
    long SequenceNumber,
    DateTimeOffset LastUpdatedAt);

/// <summary>A station by identifier and display name, for a journey's final destination.</summary>
/// <param name="Id">The station's identifier.</param>
/// <param name="Name">The station's display name, or <see langword="null"/> when the catalogue no longer holds it.</param>
public sealed record ProviderStationRefResponse(Guid Id, string? Name);
