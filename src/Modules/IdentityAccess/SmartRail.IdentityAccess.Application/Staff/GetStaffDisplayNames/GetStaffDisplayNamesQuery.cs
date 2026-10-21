using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.IdentityAccess.Application.Staff.GetStaffDisplayNames;

/// <summary>
/// The display names of the given staff accounts, keyed by staff ID. An ID with no account is left
/// out rather than failing the query.
/// </summary>
public sealed record GetStaffDisplayNamesQuery(IReadOnlyCollection<Guid> StaffIds)
    : IQuery<IReadOnlyDictionary<Guid, string>>;
