using System.Text.Json;
using SmartRail.LiveOperations.Application.JourneyEvents;
using SmartRail.LiveOperations.Application.JourneyEvents.GetEventsAfter;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.LiveOperations.UnitTests.TestDoubles;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;
using DomainJourneyEventType = SmartRail.LiveOperations.Domain.LiveState.JourneyEventType;

namespace SmartRail.LiveOperations.UnitTests.JourneyEvents;

public sealed class GetEventsAfterHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_events_after_the_position_with_their_live_snapshots()
    {
        // Arrange
        var state = TestData.RunningJourney();
        var sourceId = Guid.CreateVersion7();
        var journeyEvent = NewEvent(state, DomainJourneyEventType.DepartureLogged, sourceId);
        var repository = new FakeJourneyEventRepository { EventsToReturn = [journeyEvent] };
        var handler = new GetEventsAfterQueryHandler(repository);
        var query = new GetEventsAfterQuery(42, state.Id, 25);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var view = Assert.Single(result.Value);
        Assert.Equal(journeyEvent.Id, view.Id);
        Assert.Equal(ContractJourneyEventType.DepartureLogged, view.Type);
        Assert.Equal(state.Id, view.JourneyId);
        Assert.Equal(journeyEvent.SequenceNumber, view.SequenceNumber);
        Assert.Equal(sourceId, view.SourceId);
        Assert.Equal(state.Id, view.Snapshot.JourneyId);
        Assert.Equal(SmartRail.LiveOperations.Contracts.JourneyLifecycleStatus.Running, view.Snapshot.Status);
        Assert.Null(view.CorrectsChangeId);
        Assert.Null(view.CorrectsEventId);
        Assert.Equal((42, (Guid?)state.Id, 25), Assert.Single(repository.Queries));
        Assert.Empty(repository.CorrectionRequests);
    }

    [Fact]
    public async Task A_correction_event_includes_the_original_change_and_event_identifiers()
    {
        // Arrange
        var state = TestData.Journey();
        var correctionChangeId = Guid.CreateVersion7();
        var originalChangeId = Guid.CreateVersion7();
        var originalEventId = Guid.CreateVersion7();
        var correctionEvent = NewEvent(
            state,
            DomainJourneyEventType.CorrectionPublished,
            correctionChangeId);
        var repository = new FakeJourneyEventRepository
        {
            EventsToReturn = [correctionEvent],
            CorrectionsToReturn = new Dictionary<Guid, CorrectionReference>
            {
                [correctionChangeId] = new CorrectionReference(originalChangeId, originalEventId),
            },
        };
        var handler = new GetEventsAfterQueryHandler(repository);

        // Act
        var result = await handler.Handle(new GetEventsAfterQuery(0), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var view = Assert.Single(result.Value);
        Assert.Equal(originalChangeId, view.CorrectsChangeId);
        Assert.Equal(originalEventId, view.CorrectsEventId);
        Assert.Equal(correctionChangeId, Assert.Single(Assert.Single(repository.CorrectionRequests)));
    }

    [Fact]
    public async Task A_correction_without_a_reference_row_still_returns_the_event_with_null_links()
    {
        // Arrange
        var state = TestData.Journey();
        var correctionEvent = NewEvent(
            state,
            DomainJourneyEventType.CorrectionPublished,
            Guid.CreateVersion7());
        var repository = new FakeJourneyEventRepository { EventsToReturn = [correctionEvent] };
        var handler = new GetEventsAfterQueryHandler(repository);

        // Act
        var result = await handler.Handle(new GetEventsAfterQuery(0), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var view = Assert.Single(result.Value);
        Assert.Null(view.CorrectsChangeId);
        Assert.Null(view.CorrectsEventId);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(501, 500)]
    [InlineData(100, 100)]
    public async Task The_requested_page_size_is_clamped_to_the_supported_range(
        int requestedLimit,
        int expectedLimit)
    {
        // Arrange
        var repository = new FakeJourneyEventRepository();
        var handler = new GetEventsAfterQueryHandler(repository);

        // Act
        var result = await handler.Handle(
            new GetEventsAfterQuery(10, Limit: requestedLimit),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedLimit, Assert.Single(repository.Queries).Limit);
    }

    [Fact]
    public async Task An_event_with_a_null_snapshot_fails_instead_of_returning_an_incomplete_view()
    {
        // Arrange
        var state = TestData.Journey();
        var journeyEvent = state.RecordEvent(
            DomainJourneyEventType.DelayPublished,
            Guid.CreateVersion7(),
            TestData.Now,
            TestData.Now,
            "null");
        var repository = new FakeJourneyEventRepository { EventsToReturn = [journeyEvent] };
        var handler = new GetEventsAfterQueryHandler(repository);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new GetEventsAfterQuery(0), CancellationToken.None));

        // Assert
        Assert.Contains(journeyEvent.Id.ToString(), exception.Message);
        Assert.Contains("not a live journey snapshot", exception.Message);
    }

    [Fact]
    public async Task A_repository_failure_is_propagated()
    {
        // Arrange
        var repository = new FakeJourneyEventRepository
        {
            ExceptionToThrow = new InvalidOperationException("Events unavailable."),
        };
        var handler = new GetEventsAfterQueryHandler(repository);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new GetEventsAfterQuery(0), CancellationToken.None));

        // Assert
        Assert.Equal("Events unavailable.", exception.Message);
        Assert.Empty(repository.CorrectionRequests);
    }

    private static JourneyEvent NewEvent(
        JourneyLiveState state,
        DomainJourneyEventType eventType,
        Guid sourceId)
    {
        var snapshot = JsonSerializer.Serialize(
            LiveStateMapping.ToView(state),
            LiveJsonOptions.Default);
        return state.RecordEvent(eventType, sourceId, TestData.Now, TestData.Now, snapshot);
    }
}
