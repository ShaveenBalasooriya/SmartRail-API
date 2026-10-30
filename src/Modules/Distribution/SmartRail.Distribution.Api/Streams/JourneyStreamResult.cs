using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using SmartRail.Distribution.Application.Streams;

namespace SmartRail.Distribution.Api.Streams;

/// <summary>
/// Writes one connection's Server-Sent Events response (live-event-streams spec: public event
/// stream, Guard stream, resume, catch-up beyond the retained window and heartbeat). Each journey
/// state change becomes <c>id</c>, <c>event</c> and <c>data</c> frames, flushed as it is written; a
/// quiet connection receives a comment heartbeat so a client or a proxy can tell it is still alive.
/// </summary>
/// <remarks>
/// The framework's <c>TypedResults.ServerSentEvents</c> serialises <c>SseItem</c> frames but has no
/// way to write a comment frame, and the spec requires a comment heartbeat, so the frames are
/// written here instead. The payload is serialised with the same HTTP JSON options every other
/// public response uses, so a stream's view and the journey detail are the same document. One writer
/// serves both streams because the frame format is the same; only the filter differs (design D2).
/// </remarks>
internal sealed class JourneyStreamResult(
    IStreamHub hub,
    IStreamCatchUp catchUp,
    StreamFilter filter,
    string? lastEventId,
    TimeSpan heartbeatInterval,
    JsonSerializerOptions jsonOptions) : IResult
{
    /// <summary>The comment frame a quiet connection receives; a leading colon is an SSE comment.</summary>
    private const string HeartbeatFrame = ": keep-alive\n\n";

    /// <summary>
    /// The frame that tells a client its position cannot be resumed from, so it reloads current
    /// state from the read endpoints (live-event-streams spec, "Catch-up beyond the retained
    /// window"). It carries no ID: the event it replaces was never sent.
    /// </summary>
    private const string ResetFrame = "event: reset\ndata: {}\n\n";

    /// <summary>
    /// The directive a stream response carries: nothing about it may be stored (design D7). The
    /// public group gets it from the response middleware; the Guard stream is outside that group, so
    /// the writer sets it for every stream it serves.
    /// </summary>
    private const string NoStore = "no-store";

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var cancellationToken = httpContext.RequestAborted;

        // Registering before anything is written closes the window in which a change could be
        // committed and missed (design D4). A resume registers in buffering mode first, so what it
        // misses while it reads its catch-up is queued in order instead of overtaking the catch-up.
        using var subscription = hub.Subscribe(filter, bufferLiveEvents: lastEventId is not null);

        try
        {
            httpContext.Response.ContentType = "text/event-stream";

            // Headers are sent when the response starts, so this must be set before it does.
            httpContext.Response.Headers.CacheControl = NoStore;

            // Starts the response so the client sees the stream open. The cache directive is already
            // set, because headers are sent with the first write.
            await httpContext.Response.StartAsync(cancellationToken);

            var body = httpContext.Response.Body;
            IReadOnlySet<Guid>? sentInCatchUp = null;

            if (lastEventId is not null)
            {
                sentInCatchUp = await CatchUpAsync(subscription, body, cancellationToken);
            }

            while (true)
            {
                switch (await WaitAsync(subscription.Events, cancellationToken))
                {
                    case StreamWait.Readable:
                        while (subscription.Events.TryRead(out var envelope))
                        {
                            // A change committed before this connection registered can still be
                            // delivered by the at-least-once bus after the catch-up has sent it. The
                            // catch-up's event IDs are remembered for the life of the connection, so
                            // that late delivery is skipped instead of repeated (design D4).
                            if (sentInCatchUp?.Contains(envelope.EventId) == true)
                            {
                                continue;
                            }

                            await WriteEventAsync(body, envelope, cancellationToken);
                        }

                        break;

                    case StreamWait.Heartbeat:
                        await WriteAsync(body, HeartbeatFrame, cancellationToken);
                        break;

                    default:
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected; leaving the hub happens when the subscription is disposed.
        }
        catch (IOException)
        {
            // A dropped connection surfaces as a failed write. It ends this stream, and is not a
            // server fault worth failing the request for.
        }
    }

    /// <summary>
    /// Sends what the reconnecting client missed, then lets the queued live events through. The
    /// order is the design's buffer-then-read-then-drain: the connection was registered in
    /// buffering mode before the read, and draining skips the events the read already sent, so the
    /// join has no gap and no duplicate (design D4). Returns the event IDs the catch-up sent, which
    /// the live loop skips if the bus delivers them late.
    /// </summary>
    private async Task<IReadOnlySet<Guid>> CatchUpAsync(
        StreamSubscription subscription,
        Stream body,
        CancellationToken cancellationToken)
    {
        var missed = await catchUp.ReadAsync(filter, lastEventId, cancellationToken);

        if (missed.Reset)
        {
            await WriteAsync(body, ResetFrame, cancellationToken);
            subscription.StartLive();

            return new HashSet<Guid>();
        }

        var sent = new HashSet<Guid>(missed.Events.Count);

        foreach (var envelope in missed.Events)
        {
            await WriteEventAsync(body, envelope, cancellationToken);
            sent.Add(envelope.EventId);
        }

        subscription.StartLive(sent);

        return sent;
    }

    /// <summary>
    /// Waits for the next envelope or for the heartbeat interval, whichever comes first.
    /// </summary>
    private async Task<StreamWait> WaitAsync(
        ChannelReader<StreamEnvelope> events,
        CancellationToken cancellationToken)
    {
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        heartbeat.CancelAfter(heartbeatInterval);

        try
        {
            return await events.WaitToReadAsync(heartbeat.Token)
                ? StreamWait.Readable
                : StreamWait.Closed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StreamWait.Heartbeat;
        }
    }

    private async Task WriteEventAsync(
        Stream body,
        StreamEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(envelope.View, jsonOptions);

        var frame = $"id: {envelope.StreamPosition.ToString(CultureInfo.InvariantCulture)}\n"
            + $"event: {envelope.EventType}\n"
            + $"data: {data}\n\n";

        await WriteAsync(body, frame, cancellationToken);
    }

    /// <summary>Writes one frame and flushes it, so a client receives it as it happens.</summary>
    private static async Task WriteAsync(Stream body, string frame, CancellationToken cancellationToken)
    {
        await body.WriteAsync(Encoding.UTF8.GetBytes(frame), cancellationToken);
        await body.FlushAsync(cancellationToken);
    }

    /// <summary>What one wait ended with.</summary>
    private enum StreamWait
    {
        /// <summary>At least one envelope is waiting.</summary>
        Readable,

        /// <summary>The heartbeat interval elapsed with nothing to send.</summary>
        Heartbeat,

        /// <summary>The hub completed the connection's queue.</summary>
        Closed,
    }
}
