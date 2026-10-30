using SmartRail.RailNetwork.Domain.ValueObjects;
using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>A railway line: a coded name for an ordered set of stations.</summary>
public sealed class Line : Entity<Guid>
{
    private Line()
    {
    }

    private Line(Guid id, LineCode code, LineColor color, string name)
        : base(id)
    {
        Code = code;
        Color = color;
        Name = name;
    }

    public LineCode Code { get; private set; } = null!;

    /// <summary>The line's display colour, published so a client does not keep its own line map.</summary>
    public LineColor Color { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public ICollection<LineStation> LineStations { get; } = [];

    /// <summary>
    /// Creates a line with the identifier supplied by its seed file, so that later data can
    /// reference a known line ID.
    /// </summary>
    public static Result<Line> Create(Guid id, LineCode code, LineColor color, string name)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(color);

        if (id == Guid.Empty)
        {
            return Result.Failure<Line>(LineErrors.IdRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Line>(LineErrors.NameRequired);
        }

        return new Line(id, code, color, name.Trim());
    }
}
