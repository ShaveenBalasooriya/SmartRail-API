using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>One station's position in a line's outbound order.</summary>
public sealed class LineStation : Entity<Guid>
{
    private LineStation()
    {
    }

    private LineStation(Guid id, Guid lineId, Guid stationId, int sequence)
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

    /// <summary>Places a station on a line. Its identifier is generated with <see cref="Guid.CreateVersion7"/>.</summary>
    public static Result<LineStation> Create(Guid lineId, Guid stationId, int sequence)
    {
        if (lineId == Guid.Empty)
        {
            return Result.Failure<LineStation>(LineStationErrors.LineRequired);
        }

        if (stationId == Guid.Empty)
        {
            return Result.Failure<LineStation>(LineStationErrors.StationRequired);
        }

        if (sequence <= 0)
        {
            return Result.Failure<LineStation>(LineStationErrors.SequenceInvalid);
        }

        return new LineStation(Guid.CreateVersion7(), lineId, stationId, sequence);
    }
}
