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

    /// <summary>Creates a station. Its identifier is generated with <see cref="Guid.CreateVersion7"/>.</summary>
    public static Result<Station> Create(StationCode code, string name)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Station>(StationErrors.NameRequired);
        }

        return new Station(Guid.CreateVersion7(), code, name.Trim());
    }
}
