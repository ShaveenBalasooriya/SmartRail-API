using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.SharedKernel.Primitives;

namespace SmartRail.LiveOperations.Domain.LiveState;

/// <summary>
/// One stop of a journey's live state. The row keeps the scheduled baseline and adds what has been
/// derived from the recorded facts: expected times, the actual departure and the current platform.
/// It is a child of the <see cref="JourneyLiveState"/> aggregate and is only changed through it.
/// </summary>
public sealed class JourneyStopLiveState : Entity<Guid>
{
    private JourneyStopLiveState()
    {
    }

    private JourneyStopLiveState(
        Guid id,
        Guid journeyId,
        int sequence,
        Guid stationId,
        bool isCalling,
        DateTimeOffset? scheduledArrival,
        DateTimeOffset? scheduledDeparture,
        DateTimeOffset? expectedArrival,
        DateTimeOffset? expectedDeparture,
        DateTimeOffset? actualDeparture,
        Guid? platformId,
        DateTimeOffset? platformChangedAt)
        : base(id)
    {
        JourneyId = journeyId;
        Sequence = sequence;
        StationId = stationId;
        IsCalling = isCalling;
        ScheduledArrival = scheduledArrival;
        ScheduledDeparture = scheduledDeparture;
        ExpectedArrival = expectedArrival;
        ExpectedDeparture = expectedDeparture;
        ActualDeparture = actualDeparture;
        PlatformId = platformId;
        PlatformChangedAt = platformChangedAt;
    }

    public Guid JourneyId { get; private set; }

    public int Sequence { get; private set; }

    public Guid StationId { get; private set; }

    public bool IsCalling { get; private set; }

    public DateTimeOffset? ScheduledArrival { get; private set; }

    public DateTimeOffset? ScheduledDeparture { get; private set; }

    public DateTimeOffset? ExpectedArrival { get; private set; }

    public DateTimeOffset? ExpectedDeparture { get; private set; }

    public DateTimeOffset? ActualDeparture { get; private set; }

    /// <summary>The platform after the most recent uncorrected change, or the baseline platform.</summary>
    public Guid? PlatformId { get; private set; }

    /// <summary>When the current platform was published; <see langword="null"/> for the baseline.</summary>
    public DateTimeOffset? PlatformChangedAt { get; private set; }

    /// <summary>
    /// Creates one stored stop of a newly created journey. Its expected values come from the
    /// calculator's no-facts result, so a later recomputation matches the created state.
    /// </summary>
    internal static JourneyStopLiveState Create(Guid journeyId, StopState stop)
    {
        ArgumentNullException.ThrowIfNull(stop);

        return new JourneyStopLiveState(
            Guid.CreateVersion7(),
            journeyId,
            stop.Sequence,
            stop.StationId,
            stop.IsCalling,
            stop.ScheduledArrival,
            stop.ScheduledDeparture,
            stop.ExpectedArrival,
            stop.ExpectedDeparture,
            stop.ActualDeparture,
            stop.PlatformId,
            stop.PlatformChangedAt);
    }

    /// <summary>Replaces the derived values of this stop after a recomputation.</summary>
    internal void Update(
        DateTimeOffset? expectedArrival,
        DateTimeOffset? expectedDeparture,
        DateTimeOffset? actualDeparture,
        Guid? platformId,
        DateTimeOffset? platformChangedAt)
    {
        ExpectedArrival = expectedArrival;
        ExpectedDeparture = expectedDeparture;
        ActualDeparture = actualDeparture;
        PlatformId = platformId;
        PlatformChangedAt = platformChangedAt;
    }
}
