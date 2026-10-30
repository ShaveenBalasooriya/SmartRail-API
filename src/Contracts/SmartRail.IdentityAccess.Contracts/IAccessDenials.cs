namespace SmartRail.IdentityAccess.Contracts;

/// <summary>
/// Announces a refused write that the caller's own handler decided, rather than the authorisation
/// policy (design D6, D9). The authorisation result handler can only announce the refusals the
/// authorisation middleware produces; a handler that refuses on a business rule — for example a
/// Guard logging a departure on another Guard's journey — announces it through this port instead, so
/// the attempt still reaches the audit record as a denial.
/// </summary>
public interface IAccessDenials
{
    /// <summary>
    /// Publishes one <see cref="AccessDenied"/> through Identity's outbox and saves it. Inside a
    /// state-changing request the save joins the request transaction, which the Host commits for an
    /// answer below 500, so a 403 that follows is durable.
    /// </summary>
    /// <param name="action">The endpoint name the refusal is recorded under.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    Task AnnounceAsync(string action, CancellationToken cancellationToken = default);
}
