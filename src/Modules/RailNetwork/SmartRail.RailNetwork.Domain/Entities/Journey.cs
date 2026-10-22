using SmartRail.RailNetwork.Domain.Abstractions;
using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

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

    private Journey(
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

    /// <summary>
    /// Creates a journey for one service on one railway calendar day. Its scheduled departure is the
    /// first stop's local departure converted to a UTC instant through the clock, and its identifier
    /// is generated with <see cref="Guid.CreateVersion7"/>.
    /// </summary>
    public static Result<Journey> Create(Service service, DateOnly serviceDate, IRailwayClock clock)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        var firstDeparture = service.Stops
            .OrderBy(stop => stop.Sequence)
            .Select(stop => stop.ScheduledDeparture)
            .FirstOrDefault();

        if (firstDeparture is not { } departure)
        {
            return Result.Failure<Journey>(JourneyErrors.FirstStopDepartureRequired);
        }

        return new Journey(
            Guid.CreateVersion7(),
            service.Id,
            serviceDate,
            clock.ToUtc(serviceDate, departure),
            clock.UtcNow);
    }
}
