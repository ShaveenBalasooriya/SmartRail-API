namespace SmartRail.RailNetwork.Contracts;

/// <summary>
/// The direction a service runs in: outbound follows the line's station order, inbound the
/// reverse order (data-model.md).
/// </summary>
public enum ServiceDirection
{
    /// <summary>Follows the line's station order.</summary>
    Outbound = 0,

    /// <summary>Follows the line's station order in reverse.</summary>
    Inbound = 1,
}
