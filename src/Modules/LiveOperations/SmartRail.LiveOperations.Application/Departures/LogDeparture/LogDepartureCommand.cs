using System.Text.Json;
using MediatR;
using SmartRail.Audit.Contracts;
using SmartRail.IdentityAccess.Contracts;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.Application.Departures.LogDeparture;

/// <summary>
/// Records one departure the Guard tapped, accepting a replay of a log already stored and refusing
/// anything else exactly once (departure-logging design D1, D2, D3). Everything happens in the
/// request's transaction: the journey's live state is locked first, then the log by its client
/// identifier is looked up, then the rules run, then the log is stored, applied as a departure fact
/// and audited, so a replay produces no second row, no second event and no second audit entry.
/// </summary>
/// <param name="Id">The client-generated identifier; a resend of it must carry identical content.</param>
/// <param name="JourneyId">The journey whose stop departed.</param>
/// <param name="StopSequence">The calling stop that departed; it cannot be the journey's final stop.</param>
/// <param name="DepartedAt">The device time of the tap as text, so a missing offset is refused before binding.</param>
/// <param name="DelayCause">One of the seven causes, or <see langword="null"/> when none was given.</param>
public sealed record LogDepartureCommand(
    Guid Id,
    Guid JourneyId,
    int StopSequence,
    string? DepartedAt,
    string? DelayCause) : ICommand<LogDepartureResult>;

/// <summary>
/// The outcome of logging a departure: the stored record, and whether it was a replay (HTTP 200) or a
/// newly accepted log (HTTP 201). The app uses the difference only to clear its queue item.
/// </summary>
/// <param name="Log">The stored record, with the device time and the server receipt time.</param>
/// <param name="Replayed">True when the log had already been accepted with identical content.</param>
public sealed record LogDepartureResult(DepartureLogResponse Log, bool Replayed);

internal sealed class LogDepartureCommandHandler(
    ILiveStateRepository liveStates,
    IDepartureLogRepository departureLogs,
    IUnitOfWork unitOfWork,
    ISender sender,
    IAuditRecorder auditRecorder,
    IAccessDenials accessDenials,
    ICurrentUser currentUser,
    IRailwayCalendar calendar,
    TimeProvider timeProvider) : ICommandHandler<LogDepartureCommand, LogDepartureResult>
{
    /// <summary>The action the audit entry and a denial record; it is also the endpoint's name.</summary>
    private const string Action = "LogDeparture";

    public async Task<Result<LogDepartureResult>> Handle(
        LogDepartureCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var staffId = currentUser.StaffId
            ?? throw new InvalidOperationException(
                "A departure can only be logged by an authenticated staff member.");

        // A joined no-op inside a request: the row lock, the log, the journey event, its outbox row
        // and the audit entry are all on the request's connection and commit together (design D3).
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var state = await liveStates.GetForUpdateAsync(command.JourneyId, cancellationToken);
        if (state is null)
        {
            return Result.Failure<LogDepartureResult>(JourneyLiveStateErrors.NotFound(command.JourneyId));
        }

        // Only the assigned Guard may log the journey. The refusal is decided here, so it has to be
        // announced here for the audit record to hold it (decision D-9); the save joins the request
        // transaction, which the Host commits for a 403.
        if (state.GuardStaffId != staffId)
        {
            await accessDenials.AnnounceAsync(Action, cancellationToken);

            return Result.Failure<LogDepartureResult>(DepartureLogErrors.NotAssigned);
        }

        // The client identifier is looked up before anything is validated (design D3): a log that is
        // already stored is a replay or a conflict, whatever its content looks like now.
        var stored = await departureLogs.GetAsync(command.Id, cancellationToken);
        if (stored is not null)
        {
            return Replay(stored, command);
        }

        var departedAt = DepartureLogRules.ParseDepartedAt(command.DepartedAt);
        if (departedAt.IsFailure)
        {
            return Result.Failure<LogDepartureResult>(departedAt.Error);
        }

        var delayCause = ParseDelayCause(command.DelayCause);
        if (delayCause.IsFailure)
        {
            return Result.Failure<LogDepartureResult>(delayCause.Error);
        }

        var stop = state.CheckDepartureCanBeLogged(command.StopSequence);
        if (stop.IsFailure)
        {
            return Result.Failure<LogDepartureResult>(stop.Error);
        }

        // One clock reading serves both the plausibility bound and the receipt time, so they cannot
        // disagree by the few ticks between two reads.
        var receivedAt = ToMicroseconds(timeProvider.GetUtcNow());

        var serviceDate = DepartureLogRules.CheckServiceDate(
            state.ServiceDate,
            calendar.DayOf(receivedAt));
        if (serviceDate.IsFailure)
        {
            return Result.Failure<LogDepartureResult>(serviceDate.Error);
        }

        var deviceTime = ToMicroseconds(departedAt.Value);

        var plausible = DepartureLogRules.CheckDepartedAt(
            deviceTime,
            receivedAt,
            calendar.StartOfDay(state.ServiceDate));
        if (plausible.IsFailure)
        {
            return Result.Failure<LogDepartureResult>(plausible.Error);
        }

        var recorded = await departureLogs.GetForJourneyAsync(command.JourneyId, cancellationToken);
        var ordered = DepartureLogRules.CheckTimeOrder(command.StopSequence, deviceTime, recorded);
        if (ordered.IsFailure)
        {
            return Result.Failure<LogDepartureResult>(ordered.Error);
        }

        var created = DepartureLog.Create(
            command.Id,
            command.JourneyId,
            command.StopSequence,
            state.StopAt(command.StopSequence)!.StationId,
            staffId,
            deviceTime,
            delayCause.Value,
            receivedAt);

        if (created.IsFailure)
        {
            return Result.Failure<LogDepartureResult>(created.Error);
        }

        // The unique (journey_id, stop_sequence) index settles a second log for the same stop with a
        // different client identifier: the tap race and a duplicated queue item both end here.
        if (!await departureLogs.AddAsync(created.Value, cancellationToken))
        {
            return Result.Failure<LogDepartureResult>(DepartureLogErrors.StopAlreadyLogged);
        }

        // The reader the sink uses already sees the stored log, so the fact carries no second copy;
        // the event records the device time as when it happened and the receipt time as when the
        // server learnt of it (GRD-05).
        var applied = await sender.Send(
            new ApplyJourneyFactCommand(
                command.JourneyId,
                Fact: null,
                ContractJourneyEventType.DepartureLogged,
                command.Id,
                deviceTime,
                receivedAt),
            cancellationToken);

        if (applied.IsFailure)
        {
            return Result.Failure<LogDepartureResult>(applied.Error);
        }

        await auditRecorder.RecordAsync(
            new AuditRecordRequest(
                staffId,
                currentUser.Role,
                Action,
                TargetType: "journey",
                TargetId: command.JourneyId.ToString(),
                Before: JsonSerializer.Serialize(new
                {
                    stopSequence = command.StopSequence,
                    departedAt = (DateTimeOffset?)null,
                }),
                After: JsonSerializer.Serialize(new
                {
                    stopSequence = command.StopSequence,
                    departedAt = deviceTime,
                }),
                RelatedEntryId: null),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return Result.Success(new LogDepartureResult(ToResponse(created.Value), Replayed: false));
    }

    /// <summary>
    /// Answers a resend of a stored client identifier: identical content is a replay and returns the
    /// stored record unchanged; anything else reuses an accepted identifier and is a conflict
    /// (departure-logging spec, "Exactly-once by client ID").
    /// </summary>
    private static Result<LogDepartureResult> Replay(DepartureLog stored, LogDepartureCommand command)
    {
        var departedAt = DepartureLogRules.ParseDepartedAt(command.DepartedAt);
        var delayCause = ParseDelayCause(command.DelayCause);

        return departedAt.IsSuccess
            && delayCause.IsSuccess
            && stored.MatchesReplay(
                command.JourneyId,
                command.StopSequence,
                departedAt.Value,
                delayCause.Value)
            ? Result.Success(new LogDepartureResult(ToResponse(stored), Replayed: true))
            : Result.Failure<LogDepartureResult>(DepartureLogErrors.IdAlreadyAccepted);
    }

    /// <summary>The optional cause from the fixed list, or <see langword="null"/> when none was given.</summary>
    private static Result<DelayCause?> ParseDelayCause(string? value)
    {
        if (value is null)
        {
            return Result.Success<DelayCause?>(null);
        }

        var parsed = DelayCauses.Parse(value);

        return parsed.IsSuccess
            ? Result.Success<DelayCause?>(parsed.Value)
            : Result.Failure<DelayCause?>(parsed.Error);
    }

    /// <summary>The stored record as the response returns it.</summary>
    private static DepartureLogResponse ToResponse(DepartureLog log) => new(
        log.Id,
        log.JourneyId,
        log.StopSequence,
        ToMicroseconds(log.DepartedAt),
        log.DelayCause is { } cause ? DelayCauses.ToStorage(cause) : null,
        ToMicroseconds(log.ReceivedAt));

    /// <summary>
    /// PostgreSQL keeps timestamps to the microsecond, so times are rounded down before they are
    /// stored or compared. The response therefore shows exactly what a later replay reads back.
    /// </summary>
    private static DateTimeOffset ToMicroseconds(DateTimeOffset value)
    {
        return new DateTimeOffset(
            value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerMicrosecond),
            TimeSpan.Zero);
    }
}
