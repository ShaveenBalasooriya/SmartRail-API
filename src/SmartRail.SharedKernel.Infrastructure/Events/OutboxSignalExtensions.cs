using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SmartRail.SharedKernel.Infrastructure.Events;

/// <summary>
/// The hook a module's <c>AddDbContext</c> attaches so its save wakes the dispatcher (design D7).
/// The module registration extension is the only caller; nothing is wired into a module yet.
/// </summary>
public static class OutboxSignalExtensions
{
    /// <summary>
    /// Wakes the dispatcher after this context saves. Use it on the context that owns the module's
    /// outbox:
    /// <c>options.UseNpgsql(...).AddOutboxSignal(provider)</c>.
    /// </summary>
    public static DbContextOptionsBuilder AddOutboxSignal(
        this DbContextOptionsBuilder options,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);

        return options.AddInterceptors(services.GetRequiredService<OutboxSignalInterceptor>());
    }
}
