using SmartRail.RailNetwork.Domain.ValueObjects;
using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>A station on the network. A station on two or more lines is an interchange.</summary>
public sealed class Station : Entity<Guid>
{
    private Station()
    {
    }

    private Station(Guid id, StationCode code, string name)
        : base(id)
    {
        Code = code;
        Name = name;
    }

    public StationCode Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>
    /// Creates a station with the identifier supplied by its seed file, so that later data can
    /// reference a known station ID.
    /// </summary>
    public static Result<Station> Create(Guid id, StationCode code, string name)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (id == Guid.Empty)
        {
            return Result.Failure<Station>(StationErrors.IdRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Station>(StationErrors.NameRequired);
        }

        return new Station(id, code, name.Trim());
    }
}
