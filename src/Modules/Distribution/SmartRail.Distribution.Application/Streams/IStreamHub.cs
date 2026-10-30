namespace SmartRail.Distribution.Application.Streams;

/// <summary>
/// The in-memory set of live stream connections (design D3). Distribution keeps no subscription
/// state anywhere else: the hub is a singleton, and a connection leaves it when its subscription is
/// disposed.
/// </summary>
public interface IStreamHub
{
    /// <summary>
    /// Registers one connection and returns the handle that unregisters it.
    /// </summary>
    /// <param name="filter">What the connection subscribed to.</param>
    /// <param name="bufferLiveEvents">
    /// Whether the connection starts in buffering mode: what the hub offers it is held until
    /// <see cref="StreamSubscription.StartLive"/> drains it. A resuming connection (design D4)
    /// registers this way before it reads its catch-up, so nothing committed during the read is
    /// lost.
    /// </param>
    StreamSubscription Subscribe(StreamFilter filter, bool bufferLiveEvents = false);

    /// <summary>
    /// Whether <paramref name="eventId"/> was already streamed. Delivery is at least once, so the
    /// consumer asks this before it reads the journey again (design D3).
    /// </summary>
    bool IsHandled(Guid eventId);

    /// <summary>
    /// Offers <paramref name="envelope"/> to every connection whose filter matches and remembers
    /// the event as handled. Returns <see langword="false"/> when the event was already handled, so
    /// a redelivery sends nothing a second time.
    /// </summary>
    bool Publish(StreamEnvelope envelope);
}
