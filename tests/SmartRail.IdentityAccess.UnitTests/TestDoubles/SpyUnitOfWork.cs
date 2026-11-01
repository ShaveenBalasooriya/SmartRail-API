using SmartRail.IdentityAccess.Application.Abstractions;

namespace SmartRail.IdentityAccess.UnitTests.TestDoubles;

/// <summary>Records saves and can simulate an unavailable store.</summary>
internal sealed class SpyUnitOfWork : IUnitOfWork
{
    public int SaveChangesCount { get; private set; }

    public Exception? ExceptionToThrow { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(1);
    }
}
