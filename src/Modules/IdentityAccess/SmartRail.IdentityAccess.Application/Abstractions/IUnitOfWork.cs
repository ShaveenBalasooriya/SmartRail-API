namespace SmartRail.IdentityAccess.Application.Abstractions;

/// <summary>
/// The module's unit of work. It is implemented by the module's <c>DbContext</c> so the layer that
/// records an event commits without knowing about EF Core (design D3).
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Commits every change made in this scope.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
