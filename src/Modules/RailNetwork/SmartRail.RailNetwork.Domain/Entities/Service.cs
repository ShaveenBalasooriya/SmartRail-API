using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

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

    private Service(Guid id, Guid lineId, ServiceDirection direction, string name, Guid guardStaffId)
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

    /// <summary>
    /// Creates a service with the identifier supplied by its seed file, so that later data can
    /// reference a known service ID.
    /// </summary>
    public static Result<Service> Create(
        Guid id,
        Guid lineId,
        ServiceDirection direction,
        string name,
        Guid guardStaffId)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<Service>(ServiceErrors.IdRequired);
        }

        if (lineId == Guid.Empty)
        {
            return Result.Failure<Service>(ServiceErrors.LineRequired);
        }

        if (!Enum.IsDefined(direction))
        {
            return Result.Failure<Service>(ServiceErrors.DirectionInvalid);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Service>(ServiceErrors.NameRequired);
        }

        if (guardStaffId == Guid.Empty)
        {
            return Result.Failure<Service>(ServiceErrors.GuardRequired);
        }

        return new Service(id, lineId, direction, name.Trim(), guardStaffId);
    }
}
