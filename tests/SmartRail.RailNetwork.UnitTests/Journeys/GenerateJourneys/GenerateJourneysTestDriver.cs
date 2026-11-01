using Microsoft.Extensions.Logging.Abstractions;
using SmartRail.RailNetwork.Application.Journeys.GenerateJourneys;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.UnitTests.Journeys.GenerateJourneys;

/// <summary>
/// Builds the generation handler with every external port replaced by an in-memory double. Each test
/// owns a driver, so repository contents, announced events and transaction state stay isolated. The
/// publisher is wired into the unit of work, so an event counts as published only after a commit,
/// which is how the module's outbox behaves.
/// </summary>
internal sealed class GenerateJourneysTestDriver
{
    internal GenerateJourneysTestDriver(params Service[] services)
    {
        UnitOfWork = new FakeNetworkUnitOfWork();
        Journeys = new FakeJourneyRepository(UnitOfWork, services);
        Publisher = new SpyJourneyEventPublisher();

        UnitOfWork.RegisterAfterCommit(Publisher.MarkCommitted);
        UnitOfWork.RegisterRollback(Publisher.DiscardPending);

        Handler = new GenerateJourneysCommandHandler(
            Journeys,
            UnitOfWork,
            TestData.Clock(),
            Publisher,
            NullLogger<GenerateJourneysCommandHandler>.Instance);
    }

    internal FakeNetworkUnitOfWork UnitOfWork { get; }

    internal FakeJourneyRepository Journeys { get; }

    internal SpyJourneyEventPublisher Publisher { get; }

    internal GenerateJourneysCommandHandler Handler { get; }

    internal Task<Result> Handle() => Handler.Handle(new GenerateJourneysCommand(), CancellationToken.None);
}
