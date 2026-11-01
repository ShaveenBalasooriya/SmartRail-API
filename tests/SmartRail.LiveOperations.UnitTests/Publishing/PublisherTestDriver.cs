using SmartRail.IdentityAccess.Contracts;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.Changes;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Application.Publishing;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.LiveOperations.UnitTests.TestDoubles;

namespace SmartRail.LiveOperations.UnitTests.Publishing;

/// <summary>
/// Builds the publishing pipeline with every external port replaced by an in-memory double. Each
/// test owns a driver, so repository contents, recorded calls and transaction state are isolated.
/// </summary>
internal sealed class PublisherTestDriver
{
    internal PublisherTestDriver(JourneyLiveState? state, params StatusChange[] seededChanges)
    {
        StaffId = Guid.CreateVersion7();
        UnitOfWork = new FakeUnitOfWork();
        LiveStates = state is null ? new FakeLiveStateRepository() : new FakeLiveStateRepository(state);
        Changes = new FakeStatusChangeRepository(UnitOfWork, seededChanges);
        Sender = new SpySender();
        Audit = new SpyAuditRecorder();
        CurrentUser = new StubCurrentUser(StaffId);
        Staff = new StubStaffDirectory(new Dictionary<Guid, string> { [StaffId] = "Controller One" });
        Calendar = new StubRailwayCalendar();
    }

    internal Guid StaffId { get; }

    internal FakeUnitOfWork UnitOfWork { get; }

    internal FakeLiveStateRepository LiveStates { get; }

    internal FakeStatusChangeRepository Changes { get; }

    internal SpySender Sender { get; }

    internal SpyAuditRecorder Audit { get; }

    internal StubCurrentUser CurrentUser { get; set; }

    internal StubStaffDirectory Staff { get; }

    internal StubRailwayCalendar Calendar { get; }

    internal StatusChangePublisher CreatePublisher() => new(
        LiveStates,
        Changes,
        UnitOfWork,
        Sender,
        Audit,
        CurrentUser,
        Calendar,
        Staff,
        new StubTimeProvider(TestData.Now));

    internal static StatusChangePlan DelayPlan(
        JourneyLiveState state,
        Guid staffId,
        int minutes = 10)
    {
        var change = TestData.Delay(state.Id, staffId, minutes: minutes);
        return new StatusChangePlan(change, "{\"delayMinutes\":0}", () => $"{{\"delayMinutes\":{minutes}}}");
    }
}
