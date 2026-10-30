namespace SmartRail.Distribution.Application.Streams;

/// <summary>
/// What one stream connection asked for (live-event-streams spec, "Public event stream" and "Guard
/// stream scoped to own journeys"): every journey, the journeys calling at one station, one journey,
/// or the journeys assigned to one guard. Every member is optional, and an empty filter matches
/// every envelope.
/// </summary>
/// <param name="StationId">The station a subscriber wants departures for, if any.</param>
/// <param name="JourneyId">The one journey a subscriber follows, if any.</param>
/// <param name="GuardStaffId">
/// The guard a subscriber follows, if any. A Guard stream sets it from the credential the connection
/// authenticated with, so the guard is identified by nothing else (live-event-streams spec, "No
/// personal data in streams").
/// </param>
public sealed record StreamFilter(Guid? StationId = null, Guid? JourneyId = null, Guid? GuardStaffId = null)
{
    /// <summary>Whether <paramref name="envelope"/> belongs on a connection with this filter.</summary>
    public bool Matches(StreamEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (JourneyId is { } journeyId && journeyId != envelope.JourneyId)
        {
            return false;
        }

        // A guard's connection carries only the journeys assigned to that guard, so another guard's
        // journey never reaches it (live-event-streams spec, "Guard stream scoped to own journeys").
        // The assignment travels on the envelope, which was built from the state the event stored,
        // so this costs no read of its own per event.
        if (GuardStaffId is { } guardStaffId && guardStaffId != envelope.GuardStaffId)
        {
            return false;
        }

        // A station subscriber sees a journey only while the journey calls at that station: a stop
        // the train passes without calling is not a departure from it (station-display-feed spec).
        return StationId is not { } stationId
            || envelope.View.Stops.Any(stop => stop.IsCalling && stop.Station.Id == stationId);
    }
}
