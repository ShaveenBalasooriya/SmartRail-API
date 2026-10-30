using System.Globalization;
using SmartRail.Distribution.Application.Public;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.Application.Streams;

/// <summary>
/// The catch-up read behind resume (design D4, D5). It decides whether the client's last event ID can
/// still be resumed from, then reads the stored events after it in stream-position order, building
/// each payload from the snapshot the event stored so a caught-up event carries the view as of that
/// change rather than the journey's state now.
/// </summary>
/// <remarks>
/// The read is paged: one batch is bounded, and batches are read until the client is caught up, so a
/// long disconnection is delivered whole instead of being silently truncated into a gap. The stored
/// events are read through Live Operations' contract; Distribution never reads its schema.
/// </remarks>
internal sealed class StreamCatchUp(ILiveJourneys liveJourneys, IPublicJourneyViews views) : IStreamCatchUp
{
    /// <summary>
    /// How many stored events one read asks for: the contract's default batch, which bounds the
    /// memory one batch holds while the catch-up pages through a longer disconnection.
    /// </summary>
    internal const int PageSize = 100;

    /// <inheritdoc />
    public async Task<StreamCatchUpResult> ReadAsync(
        StreamFilter filter,
        string? lastEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // A value that is not a whole, non-negative number names no event, so it is unknown
        // (live-event-streams spec, "Catch-up beyond the retained window").
        if (!TryReadPosition(lastEventId, out var position))
        {
            return StreamCatchUpResult.AtReset;
        }

        var oldest = await liveJourneys.GetOldestRetainedPositionAsync(cancellationToken);

        // A position below the oldest retained event has missed events that are no longer
        // available; a stream with no events has no position at all, so every ID is unknown
        // (design D5).
        if (oldest is not { } oldestPosition || position < oldestPosition)
        {
            return StreamCatchUpResult.AtReset;
        }

        if (!await IsKnownPositionAsync(position, cancellationToken))
        {
            return StreamCatchUpResult.AtReset;
        }

        return StreamCatchUpResult.From(await ReadAllAsync(filter, position, cancellationToken));
    }

    /// <summary>Reads an optional whole position; an absent value names no event.</summary>
    private static bool TryReadPosition(string? lastEventId, out long position)
    {
        position = 0;

        return !string.IsNullOrWhiteSpace(lastEventId)
            && long.TryParse(
                lastEventId.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out position)
            && position >= 0;
    }

    /// <summary>
    /// Whether a stored event sits exactly at <paramref name="position"/>. A position the stream
    /// skipped, and a position above the newest event, are both unknown, so they reset rather than
    /// quietly sending nothing (design D5).
    /// </summary>
    private async Task<bool> IsKnownPositionAsync(long position, CancellationToken cancellationToken)
    {
        var before = await liveJourneys.GetEventsAfterAsync(
            position - 1,
            journeyId: null,
            limit: 1,
            cancellationToken);

        return before.Count > 0 && before[0].StreamPosition == position;
    }

    /// <summary>
    /// Every stored event after <paramref name="position"/> that the filter matches, in position
    /// order, read one bounded batch at a time.
    /// </summary>
    private async Task<IReadOnlyList<StreamEnvelope>> ReadAllAsync(
        StreamFilter filter,
        long position,
        CancellationToken cancellationToken)
    {
        var events = new List<StreamEnvelope>();

        while (true)
        {
            var page = await liveJourneys.GetEventsAfterAsync(
                position,
                filter.JourneyId,
                PageSize,
                cancellationToken);

            if (page.Count == 0)
            {
                return events;
            }

            // The named names and the view are built once per batch, so a catch-up costs one
            // catalogue read per batch rather than one per event (design D1).
            var pageViews = await views.BuildAsync(
                [.. page.Select(stored => stored.Snapshot)],
                cancellationToken);

            for (var index = 0; index < page.Count; index++)
            {
                var stored = page[index];

                var envelope = new StreamEnvelope(
                    stored.StreamPosition,
                    stored.Id,
                    StreamEventNames.Of(stored.Type),
                    stored.JourneyId,
                    stored.Snapshot.GuardStaffId,
                    pageViews[index]);

                // The filter is the connection's own, so a station subscriber is caught up only on
                // the journeys it would have received live, and a guard only on the journeys
                // assigned to them (live-event-streams spec, "Guard stream scoped to own journeys").
                if (filter.Matches(envelope))
                {
                    events.Add(envelope);
                }
            }

            var lastPosition = page[^1].StreamPosition;

            // A short batch is the end of the stream. The position must also advance, so a misread
            // batch cannot loop forever.
            if (page.Count < PageSize || lastPosition <= position)
            {
                return events;
            }

            position = lastPosition;
        }
    }
}
