using SmartRail.SharedKernel.Primitives;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>
/// One station in a service's stopping pattern, in the service's running order. Times are local
/// times of day in the railway's time zone. A non-calling stop has no times and no platform.
/// </summary>
public sealed class ServiceStop : Entity<Guid>
{
    private ServiceStop()
    {
    }

    public ServiceStop(
        Guid id,
        Guid serviceId,
        Guid stationId,
        int sequence,
        bool isCalling,
        TimeOnly? scheduledArrival,
        TimeOnly? scheduledDeparture,
        Guid? platformId)
        : base(id)
    {
        ServiceId = serviceId;
        StationId = stationId;
        Sequence = sequence;
        IsCalling = isCalling;
        ScheduledArrival = scheduledArrival;
        ScheduledDeparture = scheduledDeparture;
        PlatformId = platformId;
    }

    public Guid ServiceId { get; private set; }

    public Guid StationId { get; private set; }

    public int Sequence { get; private set; }

    public bool IsCalling { get; private set; }

    public TimeOnly? ScheduledArrival { get; private set; }

    public TimeOnly? ScheduledDeparture { get; private set; }

    public Guid? PlatformId { get; private set; }

    public Service Service { get; private set; } = null!;

    public Station Station { get; private set; } = null!;

    public Platform? Platform { get; private set; }
}
