using SmartRail.LiveOperations.Domain.Departures;

namespace SmartRail.LiveOperations.Application.Departures;

/// <summary>
/// The port for <c>operations.departure_logs</c>, used by the logging use case to detect a replay,
/// check the time order against what is already recorded and store a new log. A stored log is not
/// itself a fact: the fact reader projects it into a departure fact when the journey's state is
/// recomputed (controller-operations package decision D-6, departure-logging design D3).
/// </summary>
public interface IDepartureLogRepository
{
    /// <summary>
    /// The log with this client-generated identifier, tracked by the current unit of work so the
    /// use case can compare a replay with the stored row in the same transaction, or
    /// <see langword="null"/> when it does not exist.
    /// </summary>
    Task<DepartureLog?> GetAsync(Guid logId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every log already stored for the journey, in stop sequence order, read without tracking.
    /// The time-order rule needs the departures on both sides of the stop being logged.
    /// </summary>
    Task<IReadOnlyList<DepartureLog>> GetForJourneyAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the log to the current unit of work and writes it in the request's transaction, so the
    /// database assigns its commit order. Returns <see langword="false"/> when the unique
    /// <c>(journey_id, stop_sequence)</c> index refuses a second log for the stop; the use case
    /// answers that conflict and the savepoint keeps the request transaction usable.
    /// </summary>
    Task<bool> AddAsync(DepartureLog log, CancellationToken cancellationToken = default);
}
