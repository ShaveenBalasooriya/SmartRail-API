using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.LiveOperations.UnitTests.TestDoubles;

namespace SmartRail.LiveOperations.UnitTests.LiveState;

/// <summary>Owns one isolated fact-application handler and its in-memory ports.</summary>
internal sealed class ApplyJourneyFactTestDriver
{
    internal ApplyJourneyFactTestDriver(JourneyLiveState? state)
    {
        UnitOfWork = new FakeUnitOfWork();
        Repository = state is null ? new FakeLiveStateRepository() : new FakeLiveStateRepository(state);
        Repository.UnitOfWork = UnitOfWork;
        FactReader = new FakeJourneyFactReader();
        Publisher = new SpyIntegrationEventPublisher();
    }

    internal FakeUnitOfWork UnitOfWork { get; }

    internal FakeLiveStateRepository Repository { get; }

    internal FakeJourneyFactReader FactReader { get; }

    internal SpyIntegrationEventPublisher Publisher { get; }

    internal ApplyJourneyFactCommandHandler CreateHandler() => new(
        Repository,
        FactReader,
        UnitOfWork,
        Publisher,
        new StubTimeProvider(TestData.Now));
}
