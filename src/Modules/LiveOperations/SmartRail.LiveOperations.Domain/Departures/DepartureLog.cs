using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Domain.Departures;

/// <summary>
/// One Guard's departure log, stored in <c>operations.departure_logs</c>. The identifier is generated
/// on the device and accepted at most once, which is what makes the offline queue's blind retries
/// idempotent (GRD-07). <see cref="DepartedAt"/> is the device time of the tap and is the
/// authoritative departure time; <see cref="ReceivedAt"/> is the server time the log arrived, kept
/// separately so a log that synced late still reports the original departure (GRD-05). A stop has at
/// most one log, enforced by the unique <c>(journey_id, stop_sequence)</c> index (GRD-02), and
/// <see cref="FactOrder"/> is assigned by the shared database sequence so departures and published
/// changes stay ordered against each other (controller-operations package decision D-6).
/// </summary>
public sealed class DepartureLog : Entity<Guid>
{
    private DepartureLog()
    {
    }

    private DepartureLog(
        Guid id,
        Guid journeyId,
        int stopSequence,
        Guid stationId,
        Guid guardStaffId,
        DateTimeOffset departedAt,
        DelayCause? delayCause,
        DateTimeOffset receivedAt)
        : base(id)
    {
        JourneyId = journeyId;
        StopSequence = stopSequence;
        StationId = stationId;
        GuardStaffId = guardStaffId;
        DepartedAt = departedAt;
        DelayCause = delayCause;
        ReceivedAt = receivedAt;
    }

    public Guid JourneyId { get; private set; }

    public int StopSequence { get; private set; }

    /// <summary>The station of the stop, copied from the journey so a log is readable on its own.</summary>
    public Guid StationId { get; private set; }

    /// <summary>The Guard who recorded the log.</summary>
    public Guid GuardStaffId { get; private set; }

    /// <summary>The device time of the tap; progress is computed from this, not from receipt.</summary>
    public DateTimeOffset DepartedAt { get; private set; }

    /// <summary>The optional reason the departure was late; <see langword="null"/> when none was given.</summary>
    public DelayCause? DelayCause { get; private set; }

    /// <summary>The server time the log was received, recorded separately from <see cref="DepartedAt"/>.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>The commit order, assigned by the database when the row is inserted.</summary>
    public long FactOrder { get; private set; }

    /// <summary>
    /// Records one departure. The client-generated identifier and the journey, stop, station and
    /// Guard are required, and an explicitly given cause has to be one of the seven values. Whether
    /// the stop may depart, and whether the timestamp is plausible, are rules against the journey's
    /// live state and the railway calendar, checked by <see cref="DepartureLogRules"/> and
    /// <c>JourneyLiveState.CheckDepartureCanBeLogged</c>.
    /// </summary>
    public static Result<DepartureLog> Create(
        Guid id,
        Guid journeyId,
        int stopSequence,
        Guid stationId,
        Guid guardStaffId,
        DateTimeOffset departedAt,
        DelayCause? delayCause,
        DateTimeOffset receivedAt)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<DepartureLog>(DepartureLogErrors.MissingId);
        }

        if (journeyId == Guid.Empty)
        {
            return Result.Failure<DepartureLog>(DepartureLogErrors.MissingJourneyId);
        }

        if (stopSequence < 1)
        {
            return Result.Failure<DepartureLog>(DepartureLogErrors.InvalidStopSequence);
        }

        if (stationId == Guid.Empty)
        {
            return Result.Failure<DepartureLog>(DepartureLogErrors.MissingStationId);
        }

        if (guardStaffId == Guid.Empty)
        {
            return Result.Failure<DepartureLog>(DepartureLogErrors.MissingGuard);
        }

        if (departedAt == default)
        {
            return Result.Failure<DepartureLog>(DepartureLogErrors.MissingDepartedAt);
        }

        if (receivedAt == default)
        {
            return Result.Failure<DepartureLog>(DepartureLogErrors.MissingReceivedAt);
        }

        if (delayCause is { } cause && !Enum.IsDefined(cause))
        {
            return Result.Failure<DepartureLog>(DepartureLogErrors.InvalidDelayCause);
        }

        return Result.Success(new DepartureLog(
            id,
            journeyId,
            stopSequence,
            stationId,
            guardStaffId,
            departedAt,
            delayCause,
            receivedAt));
    }

    /// <summary>
    /// Whether this stored log is the same as a log the app is resending: the same journey, stop,
    /// device time and delay cause, compared to the microsecond because PostgreSQL keeps timestamps
    /// to the microsecond (departure-logging design D3). A resend that matches is a replay and must
    /// change nothing; one that does not is the same client identifier reused with different content.
    /// </summary>
    public bool MatchesReplay(
        Guid journeyId,
        int stopSequence,
        DateTimeOffset departedAt,
        DelayCause? delayCause) =>
        JourneyId == journeyId
        && StopSequence == stopSequence
        && TruncateToMicrosecond(DepartedAt) == TruncateToMicrosecond(departedAt)
        && DelayCause == delayCause;

    private static DateTimeOffset TruncateToMicrosecond(DateTimeOffset value)
    {
        return new DateTimeOffset(
            value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerMicrosecond),
            TimeSpan.Zero);
    }
}
