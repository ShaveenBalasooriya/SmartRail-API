using SmartRail.SharedKernel.Primitives;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>A numbered platform at one station.</summary>
public sealed class Platform : Entity<Guid>
{
    private Platform()
    {
    }

    public Platform(Guid id, Guid stationId, string number)
        : base(id)
    {
        StationId = stationId;
        Number = number;
    }

    public Guid StationId { get; private set; }

    public string Number { get; private set; } = null!;

    public Station Station { get; private set; } = null!;
}
