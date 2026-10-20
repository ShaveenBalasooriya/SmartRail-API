using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Application.LiveState.BackfillLiveState;

/// <summary>
/// Creates live state for every generated journey of the surrounding days that lacks it (design
/// D4). It repairs a journey whose <c>JourneyGenerated</c> announcement was lost and is idempotent,
/// so it is safe to run at start-up and hourly.
/// </summary>
public sealed record BackfillLiveStateCommand : ICommand<int>;

internal sealed class BackfillLiveStateCommandHandler(
    ITimetable timetable,
    ILiveStateRepository repository,
    TimeProvider timeProvider) : ICommandHandler<BackfillLiveStateCommand, int>
{
    /// <summary>
    /// Generation creates only the railway's today and tomorrow. One day each side covers the
    /// difference between the UTC date used here and the railway's calendar date.
    /// </summary>
    private const int DaysBefore = 1;

    private const int DaysAfter = 2;

    public async Task<Result<int>> Handle(
        BackfillLiveStateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var created = 0;

        for (var offset = -DaysBefore; offset <= DaysAfter; offset++)
        {
            var journeys = await timetable.GetScheduledJourneysAsync(
                today.AddDays(offset),
                cancellationToken);

            foreach (var journey in journeys)
            {
                var now = timeProvider.GetUtcNow();
                var baseline = LiveStateBaseline.From(journey, now);
                var state = JourneyLiveState.Create(baseline, journey.ServiceId, journey.LineId, now);

                if (state.IsFailure)
                {
                    continue;
                }

                if (await repository.CreateIfMissingAsync(state.Value, cancellationToken))
                {
                    created++;
                }
            }
        }

        return Result.Success(created);
    }
}
