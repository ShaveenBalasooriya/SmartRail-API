using SmartRail.SharedKernel.Primitives;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>
/// A planned train service on one line, running in one direction every day. Its stops are the
/// line's stations in the direction's order; the assigned guard is a staff ID from another module.
/// </summary>
public sealed class Service : Entity<Guid>
{
    private Service()
    {
    }

    public Service(Guid id, Guid lineId, ServiceDirection direction, string name, Guid guardStaffId)
        : base(id)
    {
        LineId = lineId;
        Direction = direction;
        Name = name;
        GuardStaffId = guardStaffId;
    }

    public Guid LineId { get; private set; }

    public ServiceDirection Direction { get; private set; }

    public string Name { get; private set; } = null!;

    public Guid GuardStaffId { get; private set; }

    public Line Line { get; private set; } = null!;

    public ICollection<ServiceStop> Stops { get; } = [];
}
