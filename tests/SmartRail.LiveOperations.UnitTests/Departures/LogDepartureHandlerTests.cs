using System.Text.Json;
using SmartRail.LiveOperations.Application.Departures.LogDeparture;
using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.LiveOperations.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;
using JourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.UnitTests.Departures;

public sealed class LogDepartureHandlerTests
{
    [Fact]
    public async Task Happy_path_stores_and_audits_the_departure_applies_its_fact_and_returns_the_log()
    {
        // Arrange
        var guardId = Guid.CreateVersion7();
        var state = TestData.Journey(guardStaffId: guardId);
        var driver = new DepartureTestDriver(state, guardId);
        var command = NewCommand(state.Id);

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Replayed);
        var stored = Assert.Single(driver.DepartureLogs.Logs);
        Assert.Equal(command.Id, stored.Id);
        Assert.Equal(command.JourneyId, stored.JourneyId);
        Assert.Equal(command.StopSequence, stored.StopSequence);
        Assert.Equal(state.StopAt(1)!.StationId, stored.StationId);
        Assert.Equal(guardId, stored.GuardStaffId);
        Assert.Equal(DelayCause.Signalling, stored.DelayCause);
        Assert.Equal(result.Value.Log.DepartedAt, stored.DepartedAt);
        Assert.Equal(TestData.Now, result.Value.Log.ReceivedAt);

        var fact = Assert.Single(driver.Sender.Commands);
        Assert.Equal(state.Id, fact.JourneyId);
        Assert.Equal(JourneyEventType.DepartureLogged, fact.EventType);
        Assert.Equal(command.Id, fact.SourceId);
        Assert.Equal(stored.DepartedAt, fact.OccurredAt);
        Assert.Equal(TestData.Now, fact.RecordedAt);

        var audit = Assert.Single(driver.Audit.Requests);
        Assert.Equal(guardId, audit.ActorStaffId);
        Assert.Equal("guard", audit.ActorRole);
        Assert.Equal("LogDeparture", audit.Action);
        Assert.Equal("journey", audit.TargetType);
        Assert.Equal(state.Id.ToString(), audit.TargetId);
        Assert.Equal(
            JsonSerializer.Serialize(new { stopSequence = 1, departedAt = (DateTimeOffset?)null }),
            audit.Before);
        Assert.Equal(
            JsonSerializer.Serialize(new { stopSequence = 1, departedAt = stored.DepartedAt }),
            audit.After);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task An_identical_resend_returns_the_stored_log_without_writing_or_auditing_again()
    {
        // Arrange
        var guardId = Guid.CreateVersion7();
        var state = TestData.Journey(guardStaffId: guardId);
        var command = NewCommand(state.Id);
        var stored = TestData.Departure(
            command.Id,
            state.Id,
            guardId,
            departedAt: DateTimeOffset.Parse(command.DepartedAt!),
            delayCause: DelayCause.Signalling);
        var driver = new DepartureTestDriver(state, guardId, stored);

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Replayed);
        Assert.Equal(stored.Id, result.Value.Log.Id);
        Assert.Single(driver.DepartureLogs.Logs);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_resend_with_different_content_conflicts_without_adding_another_log()
    {
        // Arrange
        var guardId = Guid.CreateVersion7();
        var state = TestData.Journey(guardStaffId: guardId);
        var original = NewCommand(state.Id);
        var stored = TestData.Departure(
            original.Id,
            state.Id,
            guardId,
            departedAt: DateTimeOffset.Parse(original.DepartedAt!),
            delayCause: DelayCause.Signalling);
        var driver = new DepartureTestDriver(state, guardId, stored);
        var conflicting = original with { StopSequence = 2 };

        // Act
        var result = await driver.CreateHandler().Handle(conflicting, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.IdAlreadyAccepted, result.Error);
        Assert.Single(driver.DepartureLogs.Logs);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
    }

    [Fact]
    public async Task A_missing_journey_returns_not_found_before_any_departure_is_written()
    {
        // Arrange
        var guardId = Guid.CreateVersion7();
        var driver = new DepartureTestDriver(state: null, guardId);
        var command = NewCommand(Guid.CreateVersion7());

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(driver.DepartureLogs.Logs);
        Assert.Empty(driver.Audit.Requests);
    }

    [Fact]
    public async Task A_guard_who_is_not_assigned_to_the_journey_is_denied_and_the_denial_is_announced()
    {
        // Arrange
        var assignedGuardId = Guid.CreateVersion7();
        var state = TestData.Journey(guardStaffId: assignedGuardId);
        var driver = new DepartureTestDriver(state, Guid.CreateVersion7());

        // Act
        var result = await driver.CreateHandler().Handle(NewCommand(state.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.NotAssigned, result.Error);
        Assert.Equal("LogDeparture", Assert.Single(driver.AccessDenials.Actions));
        Assert.Empty(driver.DepartureLogs.Logs);
        Assert.Empty(driver.Audit.Requests);
    }

    [Fact]
    public async Task Invalid_departure_text_is_returned_as_a_validation_failure_without_writes()
    {
        // Arrange
        var guardId = Guid.CreateVersion7();
        var state = TestData.Journey(guardStaffId: guardId);
        var driver = new DepartureTestDriver(state, guardId);
        var command = NewCommand(state.Id) with { DepartedAt = "2026-13-40T25:00:00+00:00" };

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.InvalidDepartedAt, result.Error);
        Assert.Empty(driver.DepartureLogs.Logs);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
    }

    [Fact]
    public async Task A_domain_rule_failure_is_passed_through_without_storing_the_departure()
    {
        // Arrange
        var guardId = Guid.CreateVersion7();
        var state = TestData.Journey(guardStaffId: guardId);
        var driver = new DepartureTestDriver(state, guardId);
        var command = NewCommand(state.Id) with { StopSequence = 3 };

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.StopIsFinalStop, result.Error);
        Assert.Empty(driver.DepartureLogs.Logs);
        Assert.Empty(driver.Sender.Commands);
    }

    [Fact]
    public async Task A_departure_already_recorded_at_the_stop_returns_conflict_without_an_event_or_audit()
    {
        // Arrange
        var guardId = Guid.CreateVersion7();
        var state = TestData.Journey(guardStaffId: guardId);
        var existing = TestData.Departure(journeyId: state.Id, guardStaffId: guardId);
        var driver = new DepartureTestDriver(state, guardId, existing);
        var command = NewCommand(state.Id);

        // Act
        var result = await driver.CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.StopAlreadyLogged, result.Error);
        Assert.Single(driver.DepartureLogs.Logs);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
    }

    [Fact]
    public async Task A_failed_fact_application_rolls_back_the_new_log_and_skips_the_audit()
    {
        // Arrange
        var guardId = Guid.CreateVersion7();
        var state = TestData.Journey(guardStaffId: guardId);
        var driver = new DepartureTestDriver(state, guardId);
        var failure = new Error("Test.FactFailed", "The fact could not be applied.");
        driver.Sender.FailureToReturn = failure;

        // Act
        var result = await driver.CreateHandler().Handle(NewCommand(state.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(failure, result.Error);
        Assert.Empty(driver.DepartureLogs.Logs);
        Assert.Empty(driver.Audit.Requests);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
        Assert.Equal(1, driver.UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task An_audit_failure_is_propagated_and_the_new_log_is_rolled_back()
    {
        // Arrange
        var guardId = Guid.CreateVersion7();
        var state = TestData.Journey(guardStaffId: guardId);
        var driver = new DepartureTestDriver(state, guardId);
        driver.Audit.ExceptionToThrow = new InvalidOperationException("Audit unavailable.");

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.CreateHandler().Handle(NewCommand(state.Id), CancellationToken.None));

        // Assert
        Assert.Equal("Audit unavailable.", exception.Message);
        Assert.Empty(driver.DepartureLogs.Logs);
        Assert.Single(driver.Sender.Commands);
        Assert.Single(driver.Audit.Requests);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
        Assert.Equal(1, driver.UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_start_a_departure_transaction()
    {
        // Arrange
        var guardId = Guid.CreateVersion7();
        var state = TestData.Journey(guardStaffId: guardId);
        var driver = new DepartureTestDriver(state, guardId);
        driver.CurrentUser = new StubCurrentUser(null, "guard");

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.CreateHandler().Handle(NewCommand(state.Id), CancellationToken.None));

        // Assert
        Assert.Contains("authenticated staff member", exception.Message);
        Assert.Equal(0, driver.UnitOfWork.BeginTransactionCount);
        Assert.Empty(driver.DepartureLogs.Logs);
    }

    private static LogDepartureCommand NewCommand(Guid journeyId) => new(
        Guid.CreateVersion7(),
        journeyId,
        1,
        TestData.Now.AddMinutes(-10).ToString("O"),
        "signalling");
}
