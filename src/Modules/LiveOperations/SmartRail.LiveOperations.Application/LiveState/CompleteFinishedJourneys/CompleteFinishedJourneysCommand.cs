using MediatR;
using Microsoft.Extensions.Logging;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Application.LiveState.CompleteFinishedJourneys;

/// <summary>
/// Completes every running journey that has reached its terminus (design D5). The passing of time is
/// not a recorded fact, so a periodic check is the only thing that would recompute a journey nobody
/// taps again: it applies a completion through the existing <see cref="ApplyJourneyFactCommand"/>
/// recompute, and the calculator decides whether the journey is now <c>completed</c>. A repeat run
/// appends nothing, because the state does not change and the job never loads a journey that is no
/// longer running.
/// </summary>
public sealed record CompleteFinishedJourneysCommand : ICommand<int>;

/// <summary>
/// Loads the identifiers of the <c>running</c> journeys and applies one completion fact to each, each
/// in its own transaction, so one journey failing cannot roll back or stop the others. A
/// <c>terminated</c>, <c>cancelled</c> or <c>completed</c> journey is never loaded and is never
/// touched (design D5).
/// </summary>
internal sealed class CompleteFinishedJourneysCommandHandler(
    ILiveStateRepository repository,
    ISender sender,
    TimeProvider timeProvider,
    ILogger<CompleteFinishedJourneysCommandHandler> logger)
    : ICommandHandler<CompleteFinishedJourneysCommand, int>
{
    /// <summary>
    /// The result is the number of running journeys whose completion was applied without a failure,
    /// not the number that became <c>completed</c>: the calculator decides that, and a running
    /// journey whose penultimate calling stop has no departure legitimately stays running.
    /// </summary>
    public async Task<Result<int>> Handle(
        CompleteFinishedJourneysCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var journeyIds = await repository.GetRunningJourneyIdsAsync(cancellationToken);
        var processed = 0;

        foreach (var journeyId in journeyIds)
        {
            var now = timeProvider.GetUtcNow();

            try
            {
                // A completion has no recorded fact of its own, so the fact is null: the reader
                // already returns every departure and change the state is computed from.
                var applied = await sender.Send(
                    new ApplyJourneyFactCommand(
                        journeyId,
                        Fact: null,
                        JourneyEventType.JourneyCompleted,
                        SourceId: null,
                        OccurredAt: now,
                        RecordedAt: now),
                    cancellationToken);

                if (applied.IsSuccess)
                {
                    processed++;
                }
                else
                {
                    logger.LogWarning(
                        "Journey {JourneyId} could not be checked for completion: {ErrorCode}",
                        journeyId,
                        applied.Error.Code);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // One journey failing must not stop the rest of the check; the log names the journey
                // and the exception type and carries no journey payload (PII rule).
                logger.LogError(
                    exception,
                    "Completing journey {JourneyId} failed with {ExceptionType}; the check continues",
                    journeyId,
                    exception.GetType().Name);
            }
        }

        return Result.Success(processed);
    }
}
