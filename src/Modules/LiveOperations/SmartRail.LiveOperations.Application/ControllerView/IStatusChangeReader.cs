namespace SmartRail.LiveOperations.Application.ControllerView;

/// <summary>
/// Reads the changes published on one journey for the controller's history, from
/// <c>operations.status_changes</c>.
/// </summary>
public interface IStatusChangeReader
{
    /// <summary>The changes published on the journey, in publish order.</summary>
    Task<IReadOnlyList<StatusChangeView>> GetForJourneyAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default);
}

/// <summary>A published change as the reader supplies it, before the publisher's name is resolved.</summary>
/// <param name="Id">The change's identifier.</param>
/// <param name="Type">The kind of change.</param>
/// <param name="Values">The values the change set, keyed by field name.</param>
/// <param name="PublisherStaffId">The controller account that published the change.</param>
/// <param name="PublishedAt">When the change was published.</param>
/// <param name="Provenance">The label that says how the change reached the channels.</param>
/// <param name="Corrected">True when a later correction has withdrawn the change.</param>
public sealed record StatusChangeView(
    Guid Id,
    string Type,
    IReadOnlyDictionary<string, string?> Values,
    Guid PublisherStaffId,
    DateTimeOffset PublishedAt,
    string Provenance,
    bool Corrected);
