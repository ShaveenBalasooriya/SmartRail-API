namespace SmartRail.Distribution.Application.Streams;

/// <summary>
/// Reads what a reconnecting client missed, so its stream picks up where it left off (live-event-streams
/// spec "Resume from last event" and "Catch-up beyond the retained window"; design D4, D5).
/// </summary>
public interface IStreamCatchUp
{
    /// <summary>
    /// The events after the position in <paramref name="lastEventId"/> that match
    /// <paramref name="filter"/>, in stream-position order, or a reset when that position can no
    /// longer be resumed from.
    /// </summary>
    /// <param name="filter">What the connection subscribed to; events it would not receive live are left out.</param>
    /// <param name="lastEventId">The <c>Last-Event-ID</c> the client sent, unparsed.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    Task<StreamCatchUpResult> ReadAsync(
        StreamFilter filter,
        string? lastEventId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What one catch-up read found: either the events to send before going live, or the decision that
/// the client's position cannot be resumed and it must reload current state from the read endpoints
/// (live-event-streams spec, "Catch-up beyond the retained window").
/// </summary>
/// <param name="Reset">Whether the client must be told to reload instead of being caught up.</param>
/// <param name="Events">The events to send, in stream-position order, when no reset is needed.</param>
public sealed record StreamCatchUpResult(bool Reset, IReadOnlyList<StreamEnvelope> Events)
{
    /// <summary>The client's position cannot be resumed, so the stream starts with a reset event.</summary>
    public static StreamCatchUpResult AtReset { get; } = new(Reset: true, Events: []);

    /// <summary>The events the client missed, in stream-position order.</summary>
    public static StreamCatchUpResult From(IReadOnlyList<StreamEnvelope> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return new StreamCatchUpResult(Reset: false, events);
    }
}
