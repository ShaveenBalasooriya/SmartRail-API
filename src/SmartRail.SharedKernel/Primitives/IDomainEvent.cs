namespace SmartRail.SharedKernel.Primitives;

/// <summary>
/// A domain event records that something happened inside a module. It is an in-process
/// notification: the module that raised it does not know who handles it.
/// </summary>
public interface IDomainEvent
{
    /// <summary>The instant the event happened, taken from <see cref="TimeProvider"/>.</summary>
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Base record for domain events, so every event carries its occurrence time.
/// </summary>
public abstract record DomainEvent(DateTimeOffset OccurredAt) : IDomainEvent;
