using SmartRail.SharedKernel.Primitives;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>
/// One dated instance of a service: the operational journey passengers follow and staff work.
/// There is at most one journey per service per date.
/// </summary>
public sealed class Journey : Entity<Guid>
{
    private Journey()
    {
    }

    public Journey(
        Guid id,
        Guid serviceId,
        DateOnly serviceDate,
        DateTimeOffset scheduledDepartureAt,
        DateTimeOffset createdAt)
        : base(id)
    {
        ServiceId = serviceId;
        ServiceDate = serviceDate;
        ScheduledDepartureAt = scheduledDepartureAt;
        CreatedAt = createdAt;
    }

    public Guid ServiceId { get; private set; }

    public DateOnly ServiceDate { get; private set; }

    /// <summary>The UTC instant of the service's first stop's scheduled departure.</summary>
    public DateTimeOffset ScheduledDepartureAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Service Service { get; private set; } = null!;
}
