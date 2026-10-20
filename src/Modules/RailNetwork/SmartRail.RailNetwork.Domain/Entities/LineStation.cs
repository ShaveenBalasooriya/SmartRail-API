using SmartRail.SharedKernel.Primitives;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>One station's position in a line's outbound order.</summary>
public sealed class LineStation : Entity<Guid>
{
    private LineStation()
    {
    }

    public LineStation(Guid id, Guid lineId, Guid stationId, int sequence)
        : base(id)
    {
        LineId = lineId;
        StationId = stationId;
        Sequence = sequence;
    }

    public Guid LineId { get; private set; }

    public Guid StationId { get; private set; }

    public int Sequence { get; private set; }

    public Line Line { get; private set; } = null!;

    public Station Station { get; private set; } = null!;
}
