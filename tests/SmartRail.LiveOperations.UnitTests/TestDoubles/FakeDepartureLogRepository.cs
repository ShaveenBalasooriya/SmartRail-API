using SmartRail.LiveOperations.Application.Departures;
using SmartRail.LiveOperations.Domain.Departures;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>
/// An in-memory departure-log repository. Adds participate in the fake request transaction and the
/// journey and stop uniqueness rule returns false just as the real repository does.
/// </summary>
internal sealed class FakeDepartureLogRepository : IDepartureLogRepository
{
    private readonly FakeUnitOfWork? _unitOfWork;
    private readonly List<DepartureLog> _logs;

    public FakeDepartureLogRepository(FakeUnitOfWork? unitOfWork = null, params DepartureLog[] logs)
    {
        _unitOfWork = unitOfWork;
        _logs = [.. logs];
    }

    public IReadOnlyList<DepartureLog> Logs => _logs;

    public Exception? AddException { get; set; }

    public Task<DepartureLog?> GetAsync(Guid logId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_logs.SingleOrDefault(log => log.Id == logId));

    public Task<IReadOnlyList<DepartureLog>> GetForJourneyAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DepartureLog> result = [.. _logs
            .Where(log => log.JourneyId == journeyId)
            .OrderBy(log => log.StopSequence)];
        return Task.FromResult(result);
    }

    public Task<bool> AddAsync(DepartureLog log, CancellationToken cancellationToken = default)
    {
        if (AddException is not null)
        {
            throw AddException;
        }

        if (_logs.Any(stored => stored.JourneyId == log.JourneyId
                && stored.StopSequence == log.StopSequence))
        {
            return Task.FromResult(false);
        }

        _logs.Add(log);
        _unitOfWork?.RegisterRollback(() => _logs.Remove(log));
        return Task.FromResult(true);
    }
}
