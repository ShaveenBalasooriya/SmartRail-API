using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SmartRail.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// Enlists a module's <c>DbContext</c> in the request transaction just before it saves (design D1,
/// D3). It is attached to every module context through
/// <see cref="RequestTransactionExtensions.AddRequestTransactionEnlistment"/>.
/// </summary>
/// <remarks>
/// <c>SavingChanges</c> runs before EF Core opens a connection or starts its own transaction, which
/// is why the context can still be pointed at the request's connection here. When no request
/// transaction was begun (migrations, seeding, the dispatcher, the authorisation result handler)
/// this is a no-op.
/// </remarks>
internal sealed class RequestTransactionEnlistmentInterceptor(RequestTransaction transaction)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            transaction.Enlist(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            await transaction.EnlistAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }
}
