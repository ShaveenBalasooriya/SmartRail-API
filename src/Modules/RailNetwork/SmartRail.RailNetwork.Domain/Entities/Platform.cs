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

    /// <summary>Creates a platform. Its identifier is generated with <see cref="Guid.CreateVersion7"/>.</summary>
    public static Result<Platform> Create(Guid stationId, PlatformNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);

        if (stationId == Guid.Empty)
        {
            return Result.Failure<Platform>(PlatformErrors.StationRequired);
        }

        return new Platform(Guid.CreateVersion7(), stationId, number);
    }
}
