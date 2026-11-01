using System.Text.Json;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.LiveOperations.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;
using DomainJourneyEventType = SmartRail.LiveOperations.Domain.LiveState.JourneyEventType;

namespace SmartRail.LiveOperations.UnitTests.LiveState;

public sealed class ApplyJourneyFactHandlerTests
{
    [Fact]
    public async Task Happy_path_applies_the_fact_appends_an_event_publishes_the_outbox_announcement_and_commits()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new ApplyJourneyFactTestDriver(state);
        var fact = new DepartureFact(
            1,
            TestData.Now.AddMinutes(-10),
            TestData.Now.AddMinutes(-5),
            1);
        var sourceId = Guid.CreateVersion7();
        var command = new ApplyJourneyFactCommand(
            state.Id,
            fact,
            ContractJourneyEventType.DepartureLogged,
            sourceId,
            fact.DepartedAt,
            fact.RecordedAt);

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(SmartRail.LiveOperations.Domain.LiveState.JourneyLifecycleStatus.Running, state.Status);
        Assert.Equal(fact.DepartedAt, state.StopAt(1)!.ActualDeparture);

        var journeyEvent = Assert.Single(driver.Repository.AppendedEvents);
        Assert.Equal(state.Id, journeyEvent.JourneyId);
        Assert.Equal(1, journeyEvent.SequenceNumber);
        Assert.Equal(DomainJourneyEventType.DepartureLogged, journeyEvent.EventType);
        Assert.Equal(sourceId, journeyEvent.DepartureLogId);
        Assert.Equal(fact.DepartedAt, journeyEvent.OccurredAt);
        Assert.Equal(fact.RecordedAt, journeyEvent.RecordedAt);
        var snapshot = JsonSerializer.Deserialize<LiveJourneyView>(journeyEvent.Payload, LiveJsonOptions.Default);
        Assert.NotNull(snapshot);
        Assert.Equal(0, snapshot.LastSequenceNumber);
        Assert.Equal(SmartRail.LiveOperations.Contracts.JourneyLifecycleStatus.Running, snapshot.Status);

        var publication = Assert.Single(driver.Publisher.Publications);
        var announcement = Assert.IsType<JourneyStateChanged>(publication.IntegrationEvent);
        Assert.Equal(journeyEvent.Id, announcement.EventId);
        Assert.Equal(journeyEvent.Id, publication.EventId);
        Assert.Equal(state.Id.ToString(), publication.OrderingKey);
        Assert.Equal(state.Id, announcement.JourneyId);
        Assert.Equal(1, announcement.SequenceNumber);
        Assert.Equal(ContractJourneyEventType.DepartureLogged, announcement.EventType);
        Assert.Equal(2, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_fact_already_visible_to_the_reader_is_not_added_twice()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new ApplyJourneyFactTestDriver(state);
        var fact = new DepartureFact(1, TestData.Now.AddMinutes(-10), TestData.Now.AddMinutes(-5), 1);
        driver.FactReader.SetFacts(state.Id, fact);
        var command = new ApplyJourneyFactCommand(
            state.Id,
            fact,
            ContractJourneyEventType.DepartureLogged,
            Guid.CreateVersion7(),
            fact.DepartedAt,
            fact.RecordedAt);

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(driver.Repository.AppendedEvents);
        Assert.Equal(fact.DepartedAt, state.StopAt(1)!.ActualDeparture);
    }

    [Fact]
    public async Task A_non_publication_that_changes_nothing_appends_no_event_and_publishes_no_announcement()
    {
        // Arrange
        var state = TestData.RunningJourney();
        var fact = new DepartureFact(
            1,
            TestData.Now.AddHours(-2),
            TestData.Now.AddHours(-2).AddMinutes(1),
            1);
        var driver = new ApplyJourneyFactTestDriver(state);
        driver.FactReader.SetFacts(state.Id, fact);
        var command = new ApplyJourneyFactCommand(
            state.Id,
            null,
            ContractJourneyEventType.DepartureLogged,
            Guid.CreateVersion7(),
            fact.DepartedAt,
            fact.RecordedAt);

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(driver.Repository.AppendedEvents);
        Assert.Empty(driver.Publisher.Publications);
        Assert.Equal(0, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_published_change_appends_an_event_even_when_the_visible_state_does_not_change()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new ApplyJourneyFactTestDriver(state);
        var command = new ApplyJourneyFactCommand(
            state.Id,
            null,
            ContractJourneyEventType.DelayPublished,
            Guid.CreateVersion7(),
            TestData.Now,
            TestData.Now);

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(driver.Repository.AppendedEvents);
        Assert.Single(driver.Publisher.Publications);
        Assert.Equal(2, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_correction_marks_the_corrected_time_before_the_snapshot_is_serialized()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new ApplyJourneyFactTestDriver(state);
        var command = new ApplyJourneyFactCommand(
            state.Id,
            null,
            ContractJourneyEventType.CorrectionPublished,
            Guid.CreateVersion7(),
            TestData.Now,
            TestData.Now);

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.Now, state.CorrectedAt);
        var journeyEvent = Assert.Single(driver.Repository.AppendedEvents);
        var snapshot = JsonSerializer.Deserialize<LiveJourneyView>(journeyEvent.Payload, LiveJsonOptions.Default);
        Assert.Equal(TestData.Now, snapshot!.CorrectedAt);
    }

    [Fact]
    public async Task A_missing_journey_returns_not_found_without_reading_facts_or_publishing()
    {
        // Arrange
        var journeyId = Guid.CreateVersion7();
        var driver = new ApplyJourneyFactTestDriver(state: null);
        var command = new ApplyJourneyFactCommand(
            journeyId,
            null,
            ContractJourneyEventType.DelayPublished,
            Guid.CreateVersion7(),
            TestData.Now,
            TestData.Now);

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(driver.FactReader.RequestedJourneyIds);
        Assert.Empty(driver.Repository.AppendedEvents);
        Assert.Empty(driver.Publisher.Publications);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_fact_reader_failure_is_propagated_before_any_event_is_written()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new ApplyJourneyFactTestDriver(state);
        driver.FactReader.ExceptionToThrow = new InvalidOperationException("Facts unavailable.");

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.CreateHandler().Handle(
                new ApplyJourneyFactCommand(
                    state.Id,
                    null,
                    ContractJourneyEventType.DelayPublished,
                    Guid.CreateVersion7(),
                    TestData.Now,
                    TestData.Now),
                CancellationToken.None));

        // Assert
        Assert.Equal("Facts unavailable.", exception.Message);
        Assert.Empty(driver.Repository.AppendedEvents);
        Assert.Empty(driver.Publisher.Publications);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task An_outbox_failure_is_propagated_and_the_appended_event_is_rolled_back()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new ApplyJourneyFactTestDriver(state);
        driver.Publisher.ExceptionToThrow = new InvalidOperationException("Outbox unavailable.");

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.CreateHandler().Handle(
                new ApplyJourneyFactCommand(
                    state.Id,
                    null,
                    ContractJourneyEventType.DelayPublished,
                    Guid.CreateVersion7(),
                    TestData.Now,
                    TestData.Now),
                CancellationToken.None));

        // Assert
        Assert.Equal("Outbox unavailable.", exception.Message);
        Assert.Empty(driver.Repository.AppendedEvents);
        Assert.Single(driver.Publisher.Publications);
        Assert.Equal(1, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
        Assert.Equal(1, driver.UnitOfWork.RollbackCount);
    }
}
