using System.Text.Json;
using SmartRail.SimulatedProvider.Processing;

namespace SmartRail.SimulatedProvider.UnitTests.Processing;

/// <summary>
/// The provider's processing rules (simulated-provider spec, "Applies events by event ID and
/// sequence", "Cancellation and correction"): a duplicate changes nothing, a lower sequence number is
/// recorded but not applied, and a correction restores a cancelled journey.
/// </summary>
public sealed class JourneyStoreTests
{
    private static readonly Guid StationId = Guid.Parse("04624d0f-9410-72e3-967f-501ed71f2441");
    private static readonly Guid JourneyId = Guid.Parse("01a15874-e6e0-7f8f-8f2a-0a4b8c9d1e2f");

    private static readonly DateTimeOffset ScheduledArrival =
        new(2026, 10, 29, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_duplicate_event_is_ignored_and_changes_the_state_once()
    {
        var store = Loaded();
        var delay = Delay(eventId: Guid.CreateVersion7(), sequenceNumber: 1, minutes: 10);

        Assert.Equal("applied", store.Apply(delay));
        var afterFirst = Assert.Single(store.Snapshot().Journeys);
        Assert.Equal(ScheduledArrival.AddMinutes(10), afterFirst.ExpectedArrival);

        Assert.Equal("ignored_duplicate", store.Apply(delay));
        var afterSecond = Assert.Single(store.Snapshot().Journeys);

        Assert.Equal(afterFirst.ExpectedArrival, afterSecond.ExpectedArrival);
        Assert.Equal(afterFirst.LastAppliedSequence, afterSecond.LastAppliedSequence);
        Assert.Equal(2, store.Snapshot().ReceivedEvents.Count);
        Assert.Equal("ignored_duplicate", store.Snapshot().ReceivedEvents[^1].Outcome);
    }

    [Fact]
    public void A_lower_sequence_number_is_recorded_but_not_applied()
    {
        var store = Loaded();

        Assert.Equal("applied", store.Apply(PlatformChange(
            eventId: Guid.CreateVersion7(),
            sequenceNumber: 6,
            platformNumber: "2")));

        Assert.Equal(
            "out_of_order",
            store.Apply(Delay(eventId: Guid.CreateVersion7(), sequenceNumber: 5, minutes: 15)));

        var journey = Assert.Single(store.Snapshot().Journeys);

        // The delay of the out-of-order event is not in force, and the applied order is unchanged.
        Assert.Equal(ScheduledArrival, journey.ExpectedArrival);
        Assert.Equal(6, journey.LastAppliedSequence);
        Assert.Equal("2", journey.Platform);

        var recorded = store.Snapshot().ReceivedEvents[^1];
        Assert.Equal("out_of_order", recorded.Outcome);
        Assert.Equal(5, recorded.SequenceNumber);
    }

    [Fact]
    public void A_correction_restores_the_journey_a_cancellation_cancelled()
    {
        var store = Loaded();
        var cancellation = Cancellation(eventId: Guid.CreateVersion7(), sequenceNumber: 7);

        Assert.Equal("applied", store.Apply(cancellation));
        var cancelled = Assert.Single(store.Snapshot().Journeys);
        Assert.Equal("cancelled", cancelled.Status);
        Assert.Null(cancelled.ExpectedArrival);

        Assert.Equal(
            "applied",
            store.Apply(Correction(
                eventId: Guid.CreateVersion7(),
                sequenceNumber: 8,
                correctsEventId: cancellation.EventId)));

        var restored = Assert.Single(store.Snapshot().Journeys);
        Assert.Equal("scheduled", restored.Status);
        Assert.Equal(ScheduledArrival, restored.ExpectedArrival);
        Assert.Equal(8, restored.LastAppliedSequence);
    }

    private static JourneyStore Loaded()
    {
        var store = new JourneyStore(StationId);

        store.LoadJourneys(
            new ProviderStation(StationId, "Colombo Fort"),
            [
                new ProviderJourney(
                    JourneyId,
                    "Coastal Line",
                    "outbound",
                    Guid.CreateVersion7(),
                    "Mount Lavinia",
                    ScheduledArrival,
                    ScheduledArrival,
                    "1",
                    "scheduled",
                    SequenceNumber: 0),
            ],
            DateTimeOffset.UtcNow);

        return store;
    }

    private static ProviderWebhookEvent Delay(Guid eventId, long sequenceNumber, int minutes) =>
        NewEvent(eventId, "delay_published", sequenceNumber, values: new Dictionary<string, JsonElement>
        {
            ["delayMinutes"] = JsonSerializer.SerializeToElement(minutes),
        });

    private static ProviderWebhookEvent PlatformChange(
        Guid eventId,
        long sequenceNumber,
        string platformNumber) =>
        NewEvent(eventId, "platform_changed", sequenceNumber, values: new Dictionary<string, JsonElement>
        {
            ["platformId"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()),
            ["platformNumber"] = JsonSerializer.SerializeToElement(platformNumber),
        });

    private static ProviderWebhookEvent Cancellation(Guid eventId, long sequenceNumber) =>
        NewEvent(eventId, "cancellation_published", sequenceNumber);

    private static ProviderWebhookEvent Correction(Guid eventId, long sequenceNumber, Guid correctsEventId) =>
        NewEvent(eventId, "correction_published", sequenceNumber, correctsEventId: correctsEventId);

    private static ProviderWebhookEvent NewEvent(
        Guid eventId,
        string type,
        long sequenceNumber,
        IReadOnlyDictionary<string, JsonElement>? values = null,
        Guid? correctsEventId = null) =>
        new()
        {
            Version = 1,
            EventId = eventId,
            SequenceNumber = sequenceNumber,
            Type = type,
            JourneyId = JourneyId,
            Values = values,
            PublishedAt = DateTimeOffset.UtcNow,
            Provenance = "automatically_published",
            CorrectsEventId = correctsEventId,
        };
}
