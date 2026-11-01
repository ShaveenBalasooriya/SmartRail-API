using SmartRail.LiveOperations.Application.Publishing;
using SmartRail.LiveOperations.Domain.Changes;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>
/// An in-memory status-change repository. New rows are rolled back when the fake request transaction
/// is disposed without committing, and duplicate corrections return false like the unique index.
/// </summary>
internal sealed class FakeStatusChangeRepository : IStatusChangeRepository
{
    private readonly FakeUnitOfWork? _unitOfWork;
    private readonly List<StatusChange> _changes;

    public FakeStatusChangeRepository(FakeUnitOfWork? unitOfWork = null, params StatusChange[] changes)
    {
        _unitOfWork = unitOfWork;
        _changes = [.. changes];
    }

    public IReadOnlyList<StatusChange> Changes => _changes;

    public Exception? AddException { get; set; }

    public bool AddResult { get; set; } = true;

    public Task<bool> AddAsync(StatusChange change, CancellationToken cancellationToken = default)
    {
        if (AddException is not null)
        {
            throw AddException;
        }

        if (!AddResult || change.CorrectsChangeId is { } targetId && _changes.Any(
                stored => stored.CorrectsChangeId == targetId))
        {
            return Task.FromResult(false);
        }

        _changes.Add(change);
        _unitOfWork?.RegisterRollback(() => _changes.Remove(change));
        return Task.FromResult(true);
    }

    public Task<StatusChange?> GetAsync(Guid changeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_changes.SingleOrDefault(change => change.Id == changeId));

    public Task<bool> IsCorrectedAsync(Guid changeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_changes.Any(change => change.CorrectsChangeId == changeId));
}
