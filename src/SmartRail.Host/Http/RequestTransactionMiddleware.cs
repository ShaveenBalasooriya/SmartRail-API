using SmartRail.SharedKernel.Infrastructure.Persistence;

namespace SmartRail.Host.Http;

/// <summary>
/// Runs every state-changing request in the shared request transaction (design D1, D3): it begins
/// the transaction before the handler, commits once before the response starts when the handler
/// succeeds and rolls back when the handler throws or answers with a 5xx.
/// </summary>
/// <remarks>
/// It is added after authorisation on purpose. A refused write is answered during authorisation and
/// its outbox row is saved there, outside any request transaction, exactly as before. Reads
/// (GET, HEAD, OPTIONS) never begin a transaction and never open its connection. The commit is
/// registered as a response-starting callback, so the data is committed before any of the response
/// can reach the client (a handler that writes its response streams it out at once).
/// </remarks>
internal sealed class RequestTransactionMiddleware(
    RequestDelegate next,
    ILogger<RequestTransactionMiddleware> logger)
{
    private static readonly HashSet<string> StateChangingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
    };

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.GetEndpoint() is null || !StateChangingMethods.Contains(context.Request.Method))
        {
            await next(context);
            return;
        }

        var transaction = context.RequestServices.GetRequiredService<IRequestTransaction>();

        await transaction.BeginAsync(context.RequestAborted);

        // The commit has to happen before the response starts, not after the pipeline returns:
        // Kestrel can send the status line, headers and body as soon as the handler writes them, so
        // a client could otherwise read a 201 before the data it describes is committed, and a
        // commit that then fails could no longer change the response. The transaction ignores a
        // second commit or rollback, so the fallback below stays harmless.
        context.Response.OnStarting(() =>
        {
            return CompleteAsync(transaction, context.Response.StatusCode);
        });

        try
        {
            await next(context);

            // The response usually started inside `next`, so this is the fallback for a response
            // that never started early; the transaction ignores the repeat.
            await CompleteAsync(transaction, context.Response.StatusCode);
        }
        catch (Exception exception)
        {
            await TryRollbackAsync(transaction, exception);
            throw;
        }
        finally
        {
            // Disposing closes the connection whether the transaction committed, rolled back or
            // failed; an uncommitted Npgsql transaction rolls back when it is disposed.
            await transaction.DisposeAsync();
        }
    }

    /// <summary>
    /// Commits the request's writes when the response is a success, and rolls them back when it is a
    /// server fault, so the client never observes a response the database did not accept.
    /// </summary>
    private static Task CompleteAsync(IRequestTransaction transaction, int statusCode) =>
        statusCode >= StatusCodes.Status500InternalServerError
            ? transaction.RollbackAsync(CancellationToken.None)
            : transaction.CommitAsync(CancellationToken.None);

    /// <summary>
    /// Rolls back without replacing the failure the caller must see. If the rollback itself fails,
    /// disposing the connection in the caller's <c>finally</c> still ends the transaction.
    /// </summary>
    private async Task TryRollbackAsync(IRequestTransaction transaction, Exception exception)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            logger.LogWarning(
                rollbackException,
                "The request transaction could not be rolled back after {ExceptionType}; "
                + "disposing the connection rolls it back",
                exception.GetType().Name);
        }
    }
}
