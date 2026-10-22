namespace SmartRail.SharedKernel.Infrastructure.Events;

/// <summary>
/// One publishing module's outbox (design D3): the dispatcher needs the module's <c>DbContext</c>
/// type and schema to read and publish its rows, so each publishing module registers a source and
/// the dispatcher needs no module knowledge. It names the context by <see cref="Type"/> rather than
/// referencing Entity Framework.
/// </summary>
public interface IOutboxSource
{
    /// <summary>The module's schema; the module's transport is registered under the same key.</summary>
    string Schema { get; }

    /// <summary>The module's <c>DbContext</c> type, resolved from a fresh scope per dispatch cycle.</summary>
    Type ContextType { get; }
}
