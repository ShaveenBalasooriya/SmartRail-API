using SmartRail.Distribution.Application.Public;

namespace SmartRail.Distribution.Application.Streams;

/// <summary>
/// One event to write to a stream (live-event-streams spec, "Public event stream"). The stream
/// position is the event ID a client resumes from, the event type is the name of the frame, and the
/// view is the same public view the read endpoints return (design D1, D4).
/// </summary>
/// <param name="StreamPosition">The journey event's position in the stream, sent as the frame's ID.</param>
/// <param name="EventId">The journey event's identifier, which the hub remembers to ignore a redelivery.</param>
/// <param name="EventType">The event's name, such as <c>delay_published</c>, sent as the frame's type.</param>
/// <param name="JourneyId">The journey the event changed, which the journey filter matches.</param>
/// <param name="GuardStaffId">
/// The guard the journey is assigned to, taken from the state the event stored. It is never written
/// to a frame: it is what the Guard stream's filter matches, so a guard is sent only the events of
/// their own journeys (live-event-streams spec, "Guard stream scoped to own journeys").
/// </param>
/// <param name="View">The journey's resulting live state, in the public view's shape.</param>
public sealed record StreamEnvelope(
    long StreamPosition,
    Guid EventId,
    string EventType,
    Guid JourneyId,
    Guid GuardStaffId,
    PublicJourneyView View);
