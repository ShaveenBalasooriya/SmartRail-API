using SmartRail.RailNetwork.Domain.ValueObjects;
using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>A numbered platform at one station.</summary>
public sealed class Platform : Entity<Guid>
{
    private Platform()
    {
    }

    private Platform(Guid id, Guid stationId, PlatformNumber number)
        : base(id)
    {
        StationId = stationId;
        Number = number;
    }

    public Guid StationId { get; private set; }

    public PlatformNumber Number { get; private set; } = null!;

    public Station Station { get; private set; } = null!;

    /// <summary>
    /// Creates a platform with the identifier supplied by its seed file, so that later data can
    /// reference a known platform ID.
    /// </summary>
    public static Result<Platform> Create(Guid id, Guid stationId, PlatformNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);

        if (id == Guid.Empty)
        {
            return Result.Failure<Platform>(PlatformErrors.IdRequired);
        }

        if (stationId == Guid.Empty)
        {
            return Result.Failure<Platform>(PlatformErrors.StationRequired);
        }

        return new Platform(id, stationId, number);
    }
}
