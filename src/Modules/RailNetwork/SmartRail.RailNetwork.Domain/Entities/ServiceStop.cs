using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

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

    private ServiceStop(
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

    /// <summary>
    /// Adds a stop to a service. Its identifier is generated with <see cref="Guid.CreateVersion7"/>.
    /// Whether the stop is first or last on the service is the service's concern, so the factory
    /// only enforces what one stop can know: a calling stop needs a platform and a time, and a
    /// stop after the first needs an arrival.
    /// </summary>
    public static Result<ServiceStop> Create(
        Guid serviceId,
        Guid stationId,
        int sequence,
        bool isCalling,
        TimeOnly? scheduledArrival,
        TimeOnly? scheduledDeparture,
        Guid? platformId)
    {
        if (serviceId == Guid.Empty)
        {
            return Result.Failure<ServiceStop>(ServiceStopErrors.ServiceRequired);
        }

        if (stationId == Guid.Empty)
        {
            return Result.Failure<ServiceStop>(ServiceStopErrors.StationRequired);
        }

        if (sequence <= 0)
        {
            return Result.Failure<ServiceStop>(ServiceStopErrors.SequenceInvalid);
        }

        if (!isCalling)
        {
            if (scheduledArrival is not null || scheduledDeparture is not null || platformId is not null)
            {
                return Result.Failure<ServiceStop>(ServiceStopErrors.NonCallingStopHasDetails);
            }

            return new ServiceStop(
                Guid.CreateVersion7(),
                serviceId,
                stationId,
                sequence,
                isCalling,
                scheduledArrival: null,
                scheduledDeparture: null,
                platformId: null);
        }

        if (platformId is null)
        {
            return Result.Failure<ServiceStop>(ServiceStopErrors.PlatformRequired);
        }

        if (scheduledArrival is null && scheduledDeparture is null)
        {
            return Result.Failure<ServiceStop>(ServiceStopErrors.TimesRequired);
        }

        if (sequence > 1 && scheduledArrival is null)
        {
            return Result.Failure<ServiceStop>(ServiceStopErrors.ArrivalRequired);
        }

        return new ServiceStop(
            Guid.CreateVersion7(),
            serviceId,
            stationId,
            sequence,
            isCalling,
            scheduledArrival,
            scheduledDeparture,
            platformId);
    }
}
