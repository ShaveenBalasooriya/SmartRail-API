using SmartRail.LiveOperations.Domain.Changes;

namespace SmartRail.LiveOperations.Application.Publishing;

/// <summary>
/// The port for <c>operations.status_changes</c>. A published change is written by the save this
/// repository issues before the fact sink runs, so the database assigns its commit order and the
/// fact readers can see it (design D2, D-6). The reads serve the correction use case, which needs the
/// original change and whether it has already been withdrawn.
/// </summary>
public interface IStatusChangeRepository
{
    /// <summary>
    /// Adds the change to the current unit of work and writes it, so the database assigns its commit
    /// order before the fact sink runs. Returns <see langword="false"/> when the unique index on
    /// <c>corrects_change_id</c> refuses a second correction of the same change; the journey row lock
    /// normally answers that conflict first, so this is the backstop (change-correction spec).
    /// </summary>
    Task<bool> AddAsync(StatusChange change, CancellationToken cancellationToken = default);

    /// <summary>
    /// The change with this identifier, tracked by the current unit of work so the caller can act
    /// on it in the same transaction, or <see langword="null"/> when it does not exist.
    /// </summary>
    Task<StatusChange?> GetAsync(Guid changeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a correction on record withdraws this change. A change is corrected at most once,
    /// and a correction cannot itself be corrected (change-correction spec).
    /// </summary>
    Task<bool> IsCorrectedAsync(Guid changeId, CancellationToken cancellationToken = default);
}
