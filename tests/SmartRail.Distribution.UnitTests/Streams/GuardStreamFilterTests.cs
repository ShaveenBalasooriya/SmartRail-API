using SmartRail.Distribution.Application.Streams;
using SmartRail.Distribution.UnitTests.Public;

namespace SmartRail.Distribution.UnitTests.Streams;

/// <summary>
/// The assignment rule of the Guard stream (live-event-streams spec, "Guard stream scoped to own
/// journeys"): a filter scoped to one guard matches only the events of the journeys assigned to that
/// guard, so another guard's journey never reaches the connection, and the assignment is decided
/// from the event itself rather than from a read of every journey.
/// </summary>
public sealed class GuardStreamFilterTests
{
    private static readonly Guid Guard = Guid.CreateVersion7();

    private static readonly Guid OtherGuard = Guid.CreateVersion7();

    [Fact]
    public void A_guard_filter_receives_only_the_journeys_assigned_to_that_guard()
    {
        var hub = new StreamHub();
        var own = hub.Subscribe(new StreamFilter(GuardStaffId: Guard));

        var mine = Envelope(Guid.CreateVersion7(), Guard);
        var theirs = Envelope(Guid.CreateVersion7(), OtherGuard);

        hub.Publish(mine);
        hub.Publish(theirs);

        Assert.Equal(mine, Assert.Single(ReadAll(own)));
    }

    [Fact]
    public void The_assignment_rule_is_decided_by_the_event_and_combines_with_the_other_filters()
    {
        var journeyId = Guid.CreateVersion7();
        var mine = Envelope(journeyId, Guard);
        var theirs = Envelope(journeyId, OtherGuard);

        // The same journey ID under another guard's assignment is not this guard's event.
        Assert.True(new StreamFilter(GuardStaffId: Guard).Matches(mine));
        Assert.False(new StreamFilter(GuardStaffId: Guard).Matches(theirs));

        // A journey filter on top of the assignment narrows further, and never widens it.
        Assert.False(new StreamFilter(JourneyId: Guid.CreateVersion7(), GuardStaffId: Guard).Matches(mine));
        Assert.False(new StreamFilter(JourneyId: journeyId, GuardStaffId: OtherGuard).Matches(mine));
    }

    [Fact]
    public void An_unfiltered_connection_still_receives_a_guard_journey()
    {
        var hub = new StreamHub();
        var all = hub.Subscribe(new StreamFilter());

        hub.Publish(Envelope(Guid.CreateVersion7(), Guard));

        Assert.Single(ReadAll(all));
    }

    private static StreamEnvelope Envelope(Guid journeyId, Guid guardStaffId) =>
        new(
            StreamPosition: 1,
            EventId: Guid.CreateVersion7(),
            EventType: "delay_published",
            JourneyId: journeyId,
            GuardStaffId: guardStaffId,
            View: PublicJourneyViewFixture.View(journeyId: journeyId));

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
