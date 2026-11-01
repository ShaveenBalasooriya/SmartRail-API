using SmartRail.Distribution.Application.Streams;
using SmartRail.Distribution.UnitTests.Public;
using SmartRail.Distribution.UnitTests.TestDoubles;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.UnitTests.Streams;

/// <summary>
/// What a reconnecting stream is caught up with (live-event-streams spec "Resume from last event" and
/// "Catch-up beyond the retained window"; design D4, D5): the stored events after the client's own
/// position, in position order and filtered, or a reset when that position can no longer be resumed
/// from. The stored event's snapshot supplies the payload, so a caught-up frame shows the state as of
/// that change, not the journey now.
/// </summary>
public sealed class StreamCatchUpTests
{
    private static readonly Guid Journey = Guid.CreateVersion7();

    private static readonly Guid OtherJourney = Guid.CreateVersion7();

    [Fact]
    public async Task Happy_path_resumes_after_the_clients_last_event_and_names_each_frame()
    {
        // Arrange: the client last saw position 5, which is still retained.
        var lastSeen = TestData.Event(5, Journey);
        var delayed = TestData.Event(6, Journey);
        var cancelled = TestData.Event(7, OtherJourney, JourneyEventType.CancellationPublished);
        var journeys = new FakeLiveJourneys(lastSeen, delayed, cancelled);
        var views = new StubPublicJourneyViews();
        var catchUp = new StreamCatchUp(journeys, views);

        // Act
        var result = await catchUp.ReadAsync(new StreamFilter(), "5", CancellationToken.None);

        // Assert
        Assert.False(result.Reset);
        Assert.Equal([6L, 7L], result.Events.Select(envelope => envelope.StreamPosition));
        Assert.Equal([delayed.Id, cancelled.Id], result.Events.Select(envelope => envelope.EventId));
        Assert.Equal(["delay_published", "cancellation_published"], result.Events.Select(envelope => envelope.EventType));
        Assert.Equal([Journey, OtherJourney], result.Events.Select(envelope => envelope.JourneyId));
        Assert.Equal(delayed.Snapshot.GuardStaffId, result.Events[0].GuardStaffId);

        // The client's own position is not sent again: the read starts after it.
        Assert.DoesNotContain(lastSeen.Id, result.Events.Select(envelope => envelope.EventId));

        // The payload is the view of the snapshot the event stored, built once for the whole batch.
        Assert.Single(views.Batches);
        Assert.Same(delayed.Snapshot, views.Batches[0][0]);
        Assert.Same(cancelled.Snapshot, views.Batches[0][1]);

        // One probe asks what sits at the client's position, then one bounded batch reads the rest.
        Assert.Equal([new FakeLiveJourneys.Read(4, null, 1), new FakeLiveJourneys.Read(5, null, 100)], journeys.Reads);
    }

    [Fact]
    public async Task A_position_below_the_oldest_retained_event_resets_without_reading_the_stream()
    {
        // Arrange: the retained window starts at 5, so events before it are gone for good.
        var journeys = new FakeLiveJourneys(TestData.Event(5, Journey), TestData.Event(6, Journey));
        var views = new StubPublicJourneyViews();
        var catchUp = new StreamCatchUp(journeys, views);

        // Act
        var result = await catchUp.ReadAsync(new StreamFilter(), "3", CancellationToken.None);

        // Assert
        Assert.True(result.Reset);
        Assert.Empty(result.Events);
        Assert.Empty(journeys.Reads);
        Assert.Empty(views.Batches);
    }

    [Fact]
    public async Task A_stream_with_no_events_resets_every_position()
    {
        // Arrange: a stream with no event has no position at all, so no ID can be resumed from.
        var journeys = new FakeLiveJourneys();
        var views = new StubPublicJourneyViews();
        var catchUp = new StreamCatchUp(journeys, views);

        // Act
        var result = await catchUp.ReadAsync(new StreamFilter(), "1", CancellationToken.None);

        // Assert
        Assert.True(result.Reset);
        Assert.Empty(journeys.Reads);
        Assert.Empty(views.Batches);
    }

    [Fact]
    public async Task A_position_the_stream_skipped_resets_rather_than_sending_nothing()
    {
        // Arrange: no event sits at 2, so the client's picture is not one the stream ever had.
        var journeys = new FakeLiveJourneys(TestData.Event(1, Journey), TestData.Event(3, Journey));
        var views = new StubPublicJourneyViews();
        var catchUp = new StreamCatchUp(journeys, views);

        // Act
        var result = await catchUp.ReadAsync(new StreamFilter(), "2", CancellationToken.None);

        // Assert
        Assert.True(result.Reset);
        Assert.Empty(result.Events);
        Assert.Empty(views.Batches);
    }

    [Fact]
    public async Task A_position_above_the_newest_event_resets()
    {
        // Arrange
        var journeys = new FakeLiveJourneys(TestData.Event(1, Journey), TestData.Event(2, Journey));
        var views = new StubPublicJourneyViews();
        var catchUp = new StreamCatchUp(journeys, views);

        // Act
        var result = await catchUp.ReadAsync(new StreamFilter(), "9", CancellationToken.None);

        // Assert
        Assert.True(result.Reset);
        Assert.Empty(result.Events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("5abc")]
    [InlineData("1.5")]
    [InlineData("-1")]
    public async Task A_last_event_id_that_names_no_position_resets(string? lastEventId)
    {
        // Arrange
        var journeys = new FakeLiveJourneys(TestData.Event(5, Journey), TestData.Event(6, Journey));
        var views = new StubPublicJourneyViews();
        var catchUp = new StreamCatchUp(journeys, views);

        // Act
        var result = await catchUp.ReadAsync(new StreamFilter(), lastEventId, CancellationToken.None);

        // Assert
        Assert.True(result.Reset);
        Assert.Empty(journeys.Reads);
    }

    [Fact]
    public async Task A_padded_position_is_read_as_the_number_it_holds()
    {
        // Arrange
        var journeys = new FakeLiveJourneys(TestData.Event(5, Journey), TestData.Event(6, Journey));
        var views = new StubPublicJourneyViews();
        var catchUp = new StreamCatchUp(journeys, views);

        // Act
        var result = await catchUp.ReadAsync(new StreamFilter(), " 5 ", CancellationToken.None);

        // Assert
        Assert.False(result.Reset);
        Assert.Equal([6L], result.Events.Select(envelope => envelope.StreamPosition));
    }

    [Fact]
    public async Task A_journey_filter_is_pushed_into_the_read_so_other_journeys_are_never_built()
    {
        // Arrange: the client follows a journey that had no event since its position, so it is up to
        // date rather than out of date.
        var journeys = new FakeLiveJourneys(TestData.Event(5, Journey), TestData.Event(6, OtherJourney));
        var views = new StubPublicJourneyViews();
        var catchUp = new StreamCatchUp(journeys, views);

        // Act
        var result = await catchUp.ReadAsync(new StreamFilter(JourneyId: Journey), "5", CancellationToken.None);

        // Assert
        Assert.False(result.Reset);
        Assert.Empty(result.Events);

        // The journey is part of the read, so the other journey's event never reached the view builder.
        Assert.Empty(views.Batches);
        Assert.Equal([new FakeLiveJourneys.Read(4, null, 1), new FakeLiveJourneys.Read(5, Journey, 100)], journeys.Reads);
    }

    [Fact]
    public async Task A_station_filter_that_matches_nothing_returns_no_events_and_no_reset()
    {
        // Arrange: the subscriber follows Beta, which the fixture journey passes without calling, so
        // its stored events are read but none of them belongs on this connection.
        var journeys = new FakeLiveJourneys(TestData.Event(5, Journey), TestData.Event(6, Journey));
        var views = new StubPublicJourneyViews();
        var catchUp = new StreamCatchUp(journeys, views);

        // Act
        var result = await catchUp.ReadAsync(
            new StreamFilter(StationId: PublicJourneyViewFixture.Beta),
            "5",
            CancellationToken.None);

        // Assert
        Assert.False(result.Reset);
        Assert.Empty(result.Events);

        // A station filter cannot be pushed into the read, so the batch was read and built once and
        // the frames were then filtered on the journey's stops.
        Assert.Single(views.Batches);
    }

    [Fact]
    public async Task A_disconnection_longer_than_one_batch_is_delivered_whole()
    {
        // Arrange: 102 events after the client's position, which is more than one batch.
        var events = new List<JourneyEventView> { TestData.Event(5, Journey) };
        events.AddRange(Enumerable.Range(6, 102).Select(position => TestData.Event(position, Journey)));

        var journeys = new FakeLiveJourneys([.. events]);
        var views = new StubPublicJourneyViews();
        var catchUp = new StreamCatchUp(journeys, views);

        // Act
        var result = await catchUp.ReadAsync(new StreamFilter(), "5", CancellationToken.None);

        // Assert
        Assert.False(result.Reset);
        Assert.Equal(102, result.Events.Count);
        Assert.Equal(
            Enumerable.Range(6, 102).Select(position => (long)position),
            result.Events.Select(envelope => envelope.StreamPosition));

        // Two bounded batches were read, and each built its own views once.
        Assert.Equal(
            [new FakeLiveJourneys.Read(4, null, 1), new FakeLiveJourneys.Read(5, null, 100), new FakeLiveJourneys.Read(105, null, 100)],
            journeys.Reads);
        Assert.Equal(2, views.Batches.Count);
    }

    [Fact]
    public async Task A_failing_dependency_is_not_hidden_as_a_reset()
    {
        // Arrange
        var failure = new InvalidOperationException("The event store is unavailable.");
        var journeys = new FakeLiveJourneys(TestData.Event(5, Journey)) { ExceptionToThrow = failure };
        var catchUp = new StreamCatchUp(journeys, new StubPublicJourneyViews());

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => catchUp.ReadAsync(new StreamFilter(), "5", CancellationToken.None));

        // Assert
        Assert.Same(failure, exception);
    }
}
