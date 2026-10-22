using Microsoft.EntityFrameworkCore;

namespace SmartRail.SharedKernel.Infrastructure.Events;

/// <summary>The <see cref="IOutboxSource"/> a publishing module registers (design D3).</summary>
internal sealed class OutboxSource<TContext>(string schema) : IOutboxSource
    where TContext : DbContext
{
    public string Schema { get; } = schema;

    public Type ContextType => typeof(TContext);
}
