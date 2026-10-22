namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>
/// The direction a service runs in. <see cref="Outbound"/> follows the line's station order;
/// <see cref="Inbound"/> is the same stations in reverse.
/// </summary>
public enum ServiceDirection
{
    Outbound = 0,
    Inbound = 1,
}
