using SmartRail.Distribution.UnitTests.Public;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.UnitTests.TestDoubles;

/// <summary>One stored journey event, complete with the snapshot it carries.</summary>
internal static class TestData
{
    internal static JourneyEventView Event(
        long position,
        Guid journeyId,
        JourneyEventType type = JourneyEventType.DelayPublished,
        Guid? guardStaffId = null) =>
        new(
            Guid.CreateVersion7(),
            type,
            journeyId,
            SequenceNumber: position,
            position,
            SourceId: null,
            PublicJourneyViewFixture.At(7, 0),
            PublicJourneyViewFixture.At(7, 1),
            PublicJourneyViewFixture.Journey(journeyId: journeyId, guardStaffId: guardStaffId),
            CorrectsChangeId: null,
            CorrectsEventId: null);
}
