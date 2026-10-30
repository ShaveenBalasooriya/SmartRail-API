using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Application.LiveState.CompleteFinishedJourneys;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.SharedKernel.Results;
using JourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.UnitTests.LiveState;

/// <summary>
/// The periodic completion check's handler logic that does not need a database (design D5): it asks
/// the repository only for the running journeys, sends one completion fact per journey at the clock's
/// time, and one journey failing does not stop the rest.
/// </summary>
public sealed class CompleteFinishedJourneysHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Every_running_journey_gets_one_completion_fact_at_the_current_time()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var sender = new RecordingSender();

        var result = await NewHandler(new FakeLiveStateRepository([first, second]), sender)
            .Handle(new CompleteFinishedJourneysCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        Assert.Equal([first, second], sender.Commands.Select(command => command.JourneyId));
        Assert.All(sender.Commands, command =>
        {
            Assert.Null(command.Fact);
            Assert.Equal(JourneyEventType.JourneyCompleted, command.EventType);
            Assert.Null(command.SourceId);
            Assert.Equal(Now, command.OccurredAt);
            Assert.Equal(Now, command.RecordedAt);
        });
    }

    [Fact]
    public async Task One_journey_failing_does_not_stop_the_others()
    {
        var first = Guid.CreateVersion7();
        var failing = Guid.CreateVersion7();
        var last = Guid.CreateVersion7();
        var sender = new RecordingSender(failingJourneyId: failing);

        var result = await NewHandler(new FakeLiveStateRepository([first, failing, last]), sender)
            .Handle(new CompleteFinishedJourneysCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        Assert.Equal([first, failing, last], sender.Commands.Select(command => command.JourneyId));
    }

    private static CompleteFinishedJourneysCommandHandler NewHandler(
        ILiveStateRepository repository,
        ISender sender) =>
        new(repository, sender, new FixedTimeProvider(Now), NullLogger<CompleteFinishedJourneysCommandHandler>.Instance);

    /// <summary>Answers the identifier query; nothing else may be reached by this handler.</summary>
    private sealed class FakeLiveStateRepository(IReadOnlyList<Guid> runningJourneyIds) : ILiveStateRepository
    {
        public Task<IReadOnlyList<Guid>> GetRunningJourneyIdsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(runningJourneyIds);

        public Task<JourneyLiveState?> GetForUpdateAsync(
            Guid journeyId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The completion check only loads identifiers.");

        public Task<bool> CreateIfMissingAsync(
            JourneyLiveState state,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The completion check only loads identifiers.");

        public void Append(JourneyEvent journeyEvent) =>
            throw new NotSupportedException("The completion check appends through ApplyJourneyFact.");
    }

    /// <summary>
    /// Records each completion fact and fails the one journey it was given, so the handler's
    /// per-journey failure handling is exercised without a database.
    /// </summary>
    private sealed class RecordingSender(Guid? failingJourneyId = null) : ISender
    {
        public List<ApplyJourneyFactCommand> Commands { get; } = [];

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            var command = Assert.IsType<ApplyJourneyFactCommand>(request);
            Commands.Add(command);

            if (command.JourneyId == failingJourneyId)
            {
                throw new InvalidOperationException("Simulated failure for one journey.");
            }

            return Task.FromResult((TResponse)(object)Result.Success());
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The completion check sends one typed command.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException("The completion check sends one typed command.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The completion check sends one typed command.");

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The completion check sends one typed command.");
    }

    /// <summary>A clock frozen at one instant, so the completion time is exact.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
