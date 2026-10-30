using System.Threading.Channels;

namespace SmartRail.Distribution.Application.Streams;

/// <summary>
/// The one place live connections are registered (design D3): one unbounded channel per connection,
/// so a slow client never blocks another, and a bounded memory of recently handled event IDs, so a
/// redelivery from the at-least-once bus is not sent twice.
/// </summary>
/// <remarks>
/// There is deliberately no slow-consumer cut-off: MVP 1 serves a handful of clients with small
/// events, so a stalled connection's buffer stays small until it drops (design D3, risks).
/// </remarks>
internal sealed class StreamHub : IStreamHub
{
    /// <summary>How many recent event IDs are remembered; the set is evicted oldest first.</summary>
    internal const int RecentlyHandledCapacity = 512;

    private readonly Lock gate = new();
    private readonly HashSet<StreamSubscription> connections = [];
    private readonly HashSet<Guid> handled = [];
    private readonly Queue<Guid> handledOrder = new();

    /// <inheritdoc />
    public StreamSubscription Subscribe(StreamFilter filter, bool bufferLiveEvents = false)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var channel = Channel.CreateUnbounded<StreamEnvelope>(new UnboundedChannelOptions
        {
            // The endpoint reads the connection's channel; the dispatcher writes to every
            // connection's channel, so more than one writer is possible.
            SingleReader = true,
            SingleWriter = false,

            // A write must never continue on the writer's thread into a client's read loop.
            AllowSynchronousContinuations = false,
        });

        var subscription = new StreamSubscription(filter, channel, Unregister, bufferLiveEvents);

        lock (gate)
        {
            connections.Add(subscription);
        }

        return subscription;
    }

    /// <inheritdoc />
    public bool IsHandled(Guid eventId)
    {
        lock (gate)
        {
            return handled.Contains(eventId);
        }
    }

    /// <inheritdoc />
    public bool Publish(StreamEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        StreamSubscription[] targets;

        lock (gate)
        {
            if (!handled.Add(envelope.EventId))
            {
                return false;
            }

            handledOrder.Enqueue(envelope.EventId);

            while (handledOrder.Count > RecentlyHandledCapacity)
            {
                handled.Remove(handledOrder.Dequeue());
            }

            targets = [.. connections];
        }

        // Writing happens outside the lock: an unbounded channel accepts without waiting, and a
        // subscriber that connects or drops while this runs is not blocked either.
        foreach (var connection in targets)
        {
            if (connection.Filter.Matches(envelope))
            {
                connection.TryWrite(envelope);
            }
        }

        return true;
    }

    /// <summary>Removes one connection; the subscription completes its own channel afterwards.</summary>
    private void Unregister(StreamSubscription subscription)
    {
        lock (gate)
        {
            connections.Remove(subscription);
        }
    }
}
