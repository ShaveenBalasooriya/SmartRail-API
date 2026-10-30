using SmartRail.Audit.Contracts;
using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.Audit.Application.Entries.GetAuditEntry;

/// <summary>One entry in full, including its before and after values (AUD-03).</summary>
/// <param name="EntryId">The entry to read.</param>
public sealed record GetAuditEntryQuery(Guid EntryId) : IQuery<AuditEntryDetail>;
