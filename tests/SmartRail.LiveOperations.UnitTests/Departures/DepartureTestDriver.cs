using SmartRail.LiveOperations.Application.Departures.LogDeparture;
using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.LiveOperations.UnitTests.TestDoubles;

namespace SmartRail.LiveOperations.UnitTests.Departures;

/// <summary>Owns one isolated departure handler and all of its hand-written doubles.</summary>
internal sealed class DepartureTestDriver
{
    internal DepartureTestDriver(
        JourneyLiveState? state,
        Guid staffId,
        params DepartureLog[] storedLogs)
    {
        UnitOfWork = new FakeUnitOfWork();
        LiveStates = state is null ? new FakeLiveStateRepository() : new FakeLiveStateRepository(state);
        DepartureLogs = new FakeDepartureLogRepository(UnitOfWork, storedLogs);
        Sender = new SpySender();
        Audit = new SpyAuditRecorder();
        AccessDenials = new SpyAccessDenials();
        CurrentUser = new StubCurrentUser(staffId, "guard");
    }

    internal FakeUnitOfWork UnitOfWork { get; }

    internal FakeLiveStateRepository LiveStates { get; }

    internal FakeDepartureLogRepository DepartureLogs { get; }

    internal SpySender Sender { get; }

    internal SpyAuditRecorder Audit { get; }

    internal SpyAccessDenials AccessDenials { get; }

    internal StubCurrentUser CurrentUser { get; set; }

    internal LogDepartureCommandHandler CreateHandler() => new(
        LiveStates,
        DepartureLogs,
        UnitOfWork,
        Sender,
        Audit,
        AccessDenials,
        CurrentUser,
        new StubRailwayCalendar(),
        new StubTimeProvider(TestData.Now));
}
