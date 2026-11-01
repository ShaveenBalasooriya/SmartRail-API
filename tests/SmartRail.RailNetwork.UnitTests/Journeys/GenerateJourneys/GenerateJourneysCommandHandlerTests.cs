using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.UnitTests.TestDoubles;
using ContractDirection = SmartRail.RailNetwork.Contracts.ServiceDirection;
using ServiceDirection = SmartRail.RailNetwork.Domain.Entities.ServiceDirection;

namespace SmartRail.RailNetwork.UnitTests.Journeys.GenerateJourneys;

/// <summary>
/// The daily generation use case (journey-generation spec, design D1, D2, D9) without a database: a
/// journey per service for today and tomorrow, no duplicate when it runs twice, nothing at all when
/// there is no service, a service without a first departure skipped, and no event delivered when the
/// save fails.
/// </summary>
public sealed class GenerateJourneysCommandHandlerTests
{
    [Fact]
    public async Task Happy_path_creates_one_journey_per_service_for_today_and_tomorrow_and_announces_each_one()
    {
        // Arrange
        var outbound = TestData.TwoStopService();
        var inbound = TestData.ServiceWith(
            ServiceDirection.Inbound,
            TestData.CallingStop(1, arrival: null, departure: new TimeOnly(9, 0)),
            TestData.CallingStop(2, arrival: new TimeOnly(9, 30), departure: null));
        var driver = new GenerateJourneysTestDriver(outbound, inbound);

        // Act
        var result = await driver.Handle();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(4, driver.Journeys.Stored.Count);
        Assert.Equal(2, driver.Journeys.Stored.Count(journey => journey.ServiceId == outbound.Id));
        Assert.Equal(2, driver.Journeys.Stored.Count(journey => journey.ServiceId == inbound.Id));

        // Today and tomorrow for each service, in the order the handler loops over them.
        Assert.Equal(
            [TestData.ServiceDate, TestData.ServiceDate.AddDays(1), TestData.ServiceDate, TestData.ServiceDate.AddDays(1)],
            driver.Journeys.Stored.Select(journey => journey.ServiceDate));

        var published = driver.Publisher.Published;
        Assert.Equal(4, published.Count);
        var events = published.Select(publication => Assert.IsType<JourneyGenerated>(publication.IntegrationEvent)).ToArray();

        // One event per created journey, identified by the journey's own ID (design D2).
        Assert.Equal(driver.Journeys.Stored.Select(journey => journey.Id), events.Select(generated => generated.JourneyId));
        Assert.Equal(driver.Journeys.Stored.Select(journey => journey.Id), published.Select(publication => publication.EventId));
        Assert.All(published, publication => Assert.Null(publication.OrderingKey));

        // The event carries everything a subscriber needs to create the live state, including the
        // stop baseline converted to UTC instants.
        var todayOutbound = events[0];
        Assert.Equal(outbound.Id, todayOutbound.ServiceId);
        Assert.Equal(TestData.ServiceDate, todayOutbound.ServiceDate);
        Assert.Equal(outbound.LineId, todayOutbound.LineId);
        Assert.Equal(outbound.GuardStaffId, todayOutbound.GuardStaffId);
        Assert.Equal(ContractDirection.Outbound, todayOutbound.Direction);
        Assert.Equal(2, todayOutbound.Stops.Count);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 2, 30, 0, TimeSpan.Zero), todayOutbound.Stops[0].ScheduledDeparture);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero), todayOutbound.Stops[1].ScheduledArrival);
        Assert.Equal(outbound.Stops.First().PlatformId, todayOutbound.Stops[0].PlatformId);

        // The direction reaches the event through the contract's enum, not the domain's.
        Assert.Equal(ContractDirection.Inbound, events[2].Direction);
        Assert.Equal(TestData.ServiceDate.AddDays(1), events[1].ServiceDate);

        Assert.Equal(1, driver.UnitOfWork.BeginTransactionCount);
        Assert.Equal(1, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
        Assert.Equal(0, driver.UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Journeys_that_already_exist_are_not_duplicated_or_announced_again()
    {
        // Arrange: a previous run of the job already created both of the service's journeys.
        var service = TestData.TwoStopService();
        var clock = TestData.Clock();
        var driver = new GenerateJourneysTestDriver(service);
        driver.Journeys.Seed(Journey.Create(service, TestData.ServiceDate, clock).Value);
        driver.Journeys.Seed(Journey.Create(service, TestData.ServiceDate.AddDays(1), clock).Value);

        // Act
        var result = await driver.Handle();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, driver.Journeys.Stored.Count);
        Assert.Empty(driver.Publisher.Attempted);
        Assert.Empty(driver.Publisher.Published);

        // The transaction still runs, but nothing new was written and therefore nothing committed.
        Assert.Equal(1, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task No_services_means_no_transaction_save_or_event()
    {
        // Arrange
        var driver = new GenerateJourneysTestDriver();

        // Act
        var result = await driver.Handle();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(driver.Journeys.Stored);
        Assert.Empty(driver.Publisher.Attempted);
        Assert.Equal(0, driver.UnitOfWork.BeginTransactionCount);
        Assert.Equal(0, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_service_without_a_first_departure_is_skipped_while_the_other_services_are_generated()
    {
        // Arrange: the incomplete service's first stop only arrives, so it can never produce a journey.
        var incomplete = TestData.ServiceWith(
            TestData.CallingStop(1, arrival: new TimeOnly(8, 0), departure: null));
        var complete = TestData.TwoStopService();
        var driver = new GenerateJourneysTestDriver(incomplete, complete);

        // Act
        var result = await driver.Handle();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(driver.Journeys.Stored, journey => journey.ServiceId == incomplete.Id);
        Assert.Equal(2, driver.Journeys.Stored.Count(journey => journey.ServiceId == complete.Id));
        Assert.Equal(2, driver.Publisher.Published.Count);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_failing_save_rolls_back_every_journey_and_delivers_no_event()
    {
        // Arrange
        var driver = new GenerateJourneysTestDriver(TestData.TwoStopService());
        driver.UnitOfWork.ExceptionOnSave = new InvalidOperationException("Simulated database failure.");

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.Handle());

        // Assert: both journeys were staged and announced, then the transaction rolled back, so
        // neither the rows nor the outbox rows survive and nothing is delivered.
        Assert.Equal(2, driver.Publisher.Attempted.Count);
        Assert.Empty(driver.Publisher.Published);
        Assert.Empty(driver.Journeys.Stored);
        Assert.Equal(1, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
        Assert.Equal(1, driver.UnitOfWork.RollbackCount);
    }
}
