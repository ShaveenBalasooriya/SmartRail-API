using SmartRail.Distribution.Application.Streams;
using SmartRail.Distribution.UnitTests.Public;

namespace SmartRail.Distribution.UnitTests.Streams;

/// <summary>
/// How the live connections are kept apart (design D3): a filter decides which events a connection
/// receives, one connection's backlog never holds up another, the same event is never sent twice,
/// the memory of handled events is bounded, disposing a subscription is the one way out, and a
/// resuming connection buffers what it misses until its catch-up is drained (design D4).
/// </summary>
public sealed class StreamHubTests
{
    private static readonly Guid Journey = Guid.CreateVersion7();

    private static readonly Guid OtherJourney = Guid.CreateVersion7();

    [Fact]
    public void A_station_filter_receives_only_journeys_that_call_at_that_station()
    {
        var hub = new StreamHub();
        var calling = hub.Subscribe(new StreamFilter(StationId: PublicJourneyViewFixture.Alpha));
        var passing = hub.Subscribe(new StreamFilter(StationId: PublicJourneyViewFixture.Beta));
        var unknown = hub.Subscribe(new StreamFilter(StationId: Guid.CreateVersion7()));

        hub.Publish(Envelope(Journey));

        // Alpha is a calling stop of the fixture journey; Beta is passed without calling, so it is
        // not a departure from Beta.
        Assert.Single(ReadAll(calling));
        Assert.Empty(ReadAll(passing));
        Assert.Empty(ReadAll(unknown));
    }

    [Fact]
    public void A_journey_filter_receives_only_that_journey()
    {
        var hub = new StreamHub();
        var follower = hub.Subscribe(new StreamFilter(JourneyId: Journey));

        hub.Publish(Envelope(OtherJourney));
        hub.Publish(Envelope(Journey));

        var received = Assert.Single(ReadAll(follower));

        Assert.Equal(Journey, received.JourneyId);
    }

    [Fact]
    public void An_unfiltered_connection_receives_every_journey()
    {
        var hub = new StreamHub();
        var all = hub.Subscribe(new StreamFilter());

        hub.Publish(Envelope(Journey));
        hub.Publish(Envelope(OtherJourney));

        Assert.Equal(2, ReadAll(all).Count);
    }

    [Fact]
    public void A_connection_nobody_reads_does_not_hold_up_another()
    {
        var hub = new StreamHub();
        var stalled = hub.Subscribe(new StreamFilter());
        var reading = hub.Subscribe(new StreamFilter());

        for (var position = 1; position <= 100; position++)
        {
            hub.Publish(Envelope(Journey, position: position));
        }

        // The reader receives every event in order while the stalled connection has read nothing.
        Assert.Equal(
            Enumerable.Range(1, 100).Select(position => (long)position),
            ReadAll(reading).Select(envelope => envelope.StreamPosition));

        // The stalled connection still holds its own backlog, none of which was lost.
        Assert.Equal(100, ReadAll(stalled).Count);
    }

    [Fact]
    public void A_redelivered_event_reaches_a_connection_once()
    {
        var hub = new StreamHub();
        var connection = hub.Subscribe(new StreamFilter());
        var envelope = Envelope(Journey);

        var first = hub.Publish(envelope);
        var redelivered = hub.Publish(envelope);

        Assert.True(first);
        Assert.False(redelivered);
        Assert.Single(ReadAll(connection));
        Assert.True(hub.IsHandled(envelope.EventId));
    }

    [Fact]
    public void The_memory_of_handled_events_is_bounded()
    {
        var hub = new StreamHub();
        var oldest = Envelope(Journey, position: 1);

        hub.Publish(oldest);

        for (var position = 2; position <= StreamHub.RecentlyHandledCapacity + 1; position++)
        {
            hub.Publish(Envelope(Journey, position: position));
        }

        // The oldest event has been evicted from the bounded memory, so it is offered again; the
        // event just published is still remembered.
        Assert.False(hub.IsHandled(oldest.EventId));
        Assert.True(hub.Publish(oldest));
    }

    [Fact]
    public async Task Disposing_a_subscription_removes_the_connection_and_completes_its_queue()
    {
        var hub = new StreamHub();
        var connection = hub.Subscribe(new StreamFilter());
        var received = Envelope(Journey);

        hub.Publish(received);
        connection.Dispose();
        hub.Publish(Envelope(OtherJourney));

        Assert.Same(received, Assert.Single(ReadAll(connection)));
        Assert.False(await connection.Events.WaitToReadAsync());

        // Disposing twice is harmless: unregistering a connection that has left is a no-op.
        connection.Dispose();
    }

    [Fact]
    public void A_buffering_connection_holds_live_events_until_it_goes_live()
    {
        var hub = new StreamHub();
        var connection = hub.Subscribe(new StreamFilter(), bufferLiveEvents: true);

        hub.Publish(Envelope(Journey, position: 1));
        hub.Publish(Envelope(Journey, position: 2));

        // A resuming connection registers before it reads its catch-up, so what it misses meanwhile
        // is queued rather than written behind the catch-up.
        Assert.False(connection.Events.TryRead(out _));

        connection.StartLive();

        Assert.Equal(
            [1L, 2L],
            ReadAll(connection).Select(envelope => envelope.StreamPosition));
    }

    [Fact]
    public void A_drained_connection_skips_the_events_the_catch_up_already_sent()
    {
        var hub = new StreamHub();
        var connection = hub.Subscribe(new StreamFilter(), bufferLiveEvents: true);

        var sentInCatchUp = Envelope(Journey, position: 4);
        var alsoSentInCatchUp = Envelope(OtherJourney, position: 5);
        var stillQueued = Envelope(Journey, position: 6);

        hub.Publish(sentInCatchUp);
        hub.Publish(alsoSentInCatchUp);
        hub.Publish(stillQueued);

        connection.StartLive(new HashSet<Guid>
        {
            sentInCatchUp.EventId,
            alsoSentInCatchUp.EventId,
        });

        // The overlap at the join is removed by event ID, so the event committed during the read
        // that the catch-up already sent is not sent a second time, and no other event is dropped.
        Assert.Equal([stillQueued], ReadAll(connection));
    }

    [Fact]
    public void A_drained_connection_receives_its_buffer_before_the_events_after_it()
    {
        var hub = new StreamHub();
        var connection = hub.Subscribe(new StreamFilter(), bufferLiveEvents: true);

        hub.Publish(Envelope(Journey, position: 1));
        connection.StartLive();
        hub.Publish(Envelope(Journey, position: 2));
        hub.Publish(Envelope(Journey, position: 3));

        Assert.Equal(
            [1L, 2L, 3L],
            ReadAll(connection).Select(envelope => envelope.StreamPosition));
    }

    [Fact]
    public async Task Disposing_a_buffering_connection_completes_its_queue()
    {
        var hub = new StreamHub();
        var connection = hub.Subscribe(new StreamFilter(), bufferLiveEvents: true);

        hub.Publish(Envelope(Journey));
        connection.Dispose();

        Assert.False(await connection.Events.WaitToReadAsync());
    }

    private static StreamEnvelope Envelope(Guid journeyId, long position = 1, Guid? guardStaffId = null) =>
        new(
            position,
            Guid.CreateVersion7(),
            "delay_published",
            journeyId,
            guardStaffId ?? PublicJourneyViewFixture.GuardStaffId,
            PublicJourneyViewFixture.View(journeyId: journeyId));

    private static List<StreamEnvelope> ReadAll(StreamSubscription subscription)
    {
        var envelopes = new List<StreamEnvelope>();

        while (subscription.Events.TryRead(out var envelope))
        {
            envelopes.Add(envelope);
        }

        return envelopes;
    }
}
