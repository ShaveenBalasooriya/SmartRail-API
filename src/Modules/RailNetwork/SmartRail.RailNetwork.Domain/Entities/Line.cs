using SmartRail.SharedKernel.Primitives;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>A railway line: a coded name for an ordered set of stations.</summary>
public sealed class Line : Entity<Guid>
{
    private Line()
    {
    }

    public Line(Guid id, string code, string name)
        : base(id)
    {
        Code = code;
        Name = name;
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public ICollection<LineStation> LineStations { get; } = [];
}
