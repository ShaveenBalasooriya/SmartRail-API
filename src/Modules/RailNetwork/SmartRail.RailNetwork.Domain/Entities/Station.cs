using SmartRail.SharedKernel.Primitives;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>A station on the network. A station on two or more lines is an interchange.</summary>
public sealed class Station : Entity<Guid>
{
    private Station()
    {
    }

    public Station(Guid id, string code, string name)
        : base(id)
    {
        Code = code;
        Name = name;
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;
}
