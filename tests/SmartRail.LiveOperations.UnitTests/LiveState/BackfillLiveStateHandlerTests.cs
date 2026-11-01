using SmartRail.LiveOperations.Application.LiveState.BackfillLiveState;
using SmartRail.LiveOperations.UnitTests.TestDoubles;

namespace SmartRail.LiveOperations.UnitTests.LiveState;

public sealed class BackfillLiveStateHandlerTests
{
    [Fact]
    public async Task Happy_path_creates_only_missing_states_for_yesterday_through_the_day_after_tomorrow()
    {
        // Arrange
        var timetable = new StubTimetable();
        var states = Enumerable.Range(-1, 4)
            .Select(offset => TestData.Journey(
                serviceDate: DateOnly.FromDateTime(TestData.Now.DateTime).AddDays(offset)))
            .ToArray();
        foreach (var state in states)
        {
            timetable.SetJourneys(state.ServiceDate, TestData.TimetableJourney(state));
        }

        var repository = new FakeLiveStateRepository(states[1]);
        var handler = new BackfillLiveStateCommandHandler(
            timetable,
            repository,
            new StubTimeProvider(TestData.Now));

        // Act
        var result = await handler.Handle(new BackfillLiveStateCommand(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
        Assert.Equal(4, repository.States.Count);
        Assert.Equal(states.Select(state => state.Id).Order(), repository.States.Select(state => state.Id).Order());
        Assert.Equal(
            states.Select(state => state.ServiceDate),
            timetable.RequestedDates);
    }

    [Fact]
    public async Task A_day_with_no_scheduled_journeys_creates_nothing_and_still_checks_the_other_days()
    {
        // Arrange
        var timetable = new StubTimetable();
        var repository = new FakeLiveStateRepository();
        var handler = new BackfillLiveStateCommandHandler(
            timetable,
            repository,
            new StubTimeProvider(TestData.Now));

        // Act
        var result = await handler.Handle(new BackfillLiveStateCommand(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Empty(repository.States);
        Assert.Equal(4, timetable.RequestedDates.Count);
    }

    [Fact]
    public async Task An_invalid_timetable_journey_is_skipped_without_stopping_valid_backfills()
    {
        // Arrange
        var serviceDate = DateOnly.FromDateTime(TestData.Now.DateTime).AddDays(-1);
        var validState = TestData.Journey(serviceDate: serviceDate);
        var validJourney = TestData.TimetableJourney(validState);
        var invalidJourney = validJourney with { Id = Guid.CreateVersion7(), ServiceId = Guid.Empty };
        var timetable = new StubTimetable();
        timetable.SetJourneys(serviceDate, invalidJourney, validJourney);
        var repository = new FakeLiveStateRepository();
        var handler = new BackfillLiveStateCommandHandler(
            timetable,
            repository,
            new StubTimeProvider(TestData.Now));

        // Act
        var result = await handler.Handle(new BackfillLiveStateCommand(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        Assert.Equal(validState.Id, Assert.Single(repository.States).Id);
    }

    [Fact]
    public async Task A_repository_failure_is_propagated_instead_of_reporting_a_successful_backfill()
    {
        // Arrange
        var serviceDate = DateOnly.FromDateTime(TestData.Now.DateTime).AddDays(-1);
        var state = TestData.Journey(serviceDate: serviceDate);
        var timetable = new StubTimetable();
        timetable.SetJourneys(serviceDate, TestData.TimetableJourney(state));
        var repository = new FakeLiveStateRepository
        {
            CreateException = new InvalidOperationException("Repository unavailable."),
        };
        var handler = new BackfillLiveStateCommandHandler(
            timetable,
            repository,
            new StubTimeProvider(TestData.Now));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new BackfillLiveStateCommand(), CancellationToken.None));

        // Assert
        Assert.Equal("Repository unavailable.", exception.Message);
        Assert.Empty(repository.States);
    }
}
