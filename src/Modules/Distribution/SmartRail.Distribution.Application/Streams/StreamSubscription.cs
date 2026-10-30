using System.Threading.Channels;

namespace SmartRail.Distribution.Application.Streams;

/// <summary>
/// One connected client (design D3). It owns the connection's own channel, so writing to it never
/// waits on another connection, and disposing it is the single place a connection leaves the hub.
/// </summary>
/// <remarks>
/// A resuming connection (task 4.2, design D4) registers the same way, but in buffering mode: what
/// the hub offers while it reads its catch-up is held here rather than written to the channel, and
/// <see cref="StartLive"/> drains it once the catch-up has been sent. Registering before the read,
/// then draining with the events already sent, is what leaves no gap and no duplicate at the join.
/// </remarks>
public sealed class StreamSubscription : IDisposable
{
    private readonly Lock bufferGate = new();
    private readonly List<StreamEnvelope> buffered = [];
    private readonly Channel<StreamEnvelope> channel;
    private readonly Action<StreamSubscription> unregister;
    private bool buffering;
    private int disposed;

    internal StreamSubscription(
        StreamFilter filter,
        Channel<StreamEnvelope> channel,
        Action<StreamSubscription> unregister,
        bool bufferLiveEvents = false)
    {
        Filter = filter;
        this.channel = channel;
        this.unregister = unregister;
        buffering = bufferLiveEvents;
    }

    /// <summary>What this connection subscribed to.</summary>
    public StreamFilter Filter { get; }

    /// <summary>The envelopes the hub offered this connection, in the order it offered them.</summary>
    public ChannelReader<StreamEnvelope> Events => channel.Reader;

    /// <summary>
    /// Offers one envelope without waiting: an unbounded channel always accepts, so a slow client
    /// can never hold up another. While the connection buffers, the envelope is held in order until
    /// <see cref="StartLive"/> drains it.
    /// </summary>
    internal bool TryWrite(StreamEnvelope envelope)
    {
        lock (bufferGate)
        {
            if (buffering)
            {
                buffered.Add(envelope);
                return true;
            }
        }

        return channel.Writer.TryWrite(envelope);
    }

    /// <summary>
    /// Ends buffering and hands the connection everything the hub offered while it caught up, in the
    /// order it was offered, skipping the events the catch-up already sent. Skipping is by event ID
    /// rather than by position because the bus orders only within a journey, so an event of another
    /// journey can reach the hub out of stream-position order (design D4).
    /// </summary>
    /// <param name="alreadySent">The events the catch-up sent, or <see langword="null"/> for none.</param>
    public void StartLive(IReadOnlyCollection<Guid>? alreadySent = null)
    {
        lock (bufferGate)
        {
            if (!buffering)
            {
                return;
            }

            foreach (var envelope in buffered)
            {
                if (alreadySent is null || !alreadySent.Contains(envelope.EventId))
                {
                    channel.Writer.TryWrite(envelope);
                }
            }

            buffered.Clear();
            buffering = false;
        }
    }

    /// <summary>Leaves the hub and completes the connection's queue, so its reader ends.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        unregister(this);
        channel.Writer.TryComplete();
    }
}
