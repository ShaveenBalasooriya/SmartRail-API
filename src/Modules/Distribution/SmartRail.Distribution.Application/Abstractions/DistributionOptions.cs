namespace SmartRail.Distribution.Application.Abstractions;

/// <summary>
/// The Distribution settings read from the <c>Distribution</c> configuration section.
/// </summary>
/// <param name="RefreshAfterSeconds">
/// The interval a public response recommends a client wait before reading again, so a display can
/// show last-known data marked stale when its connection drops (display feed spec).
/// </param>
/// <param name="StreamHeartbeatSeconds">
/// How long an open stream may be quiet before a comment heartbeat is written, so a client or a
/// proxy can tell a dead connection from a quiet one (live-event-streams spec, "Heartbeat").
/// </param>
public sealed record DistributionOptions(
    int RefreshAfterSeconds,
    int StreamHeartbeatSeconds = DistributionOptions.DefaultStreamHeartbeatSeconds)
{
    /// <summary>The interval used when <c>Distribution:RefreshAfterSeconds</c> is not configured.</summary>
    public const int DefaultRefreshAfterSeconds = 15;

    /// <summary>The heartbeat used when <c>Distribution:StreamHeartbeatSeconds</c> is not configured (design D3).</summary>
    public const int DefaultStreamHeartbeatSeconds = 15;

    /// <summary>
    /// The longest heartbeat a stream may be configured with: the client must hear something at
    /// least every twenty seconds (live-event-streams spec, "Heartbeat").
    /// </summary>
    public const int MaximumStreamHeartbeatSeconds = 20;
}
