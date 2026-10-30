using SmartRail.Audit.Contracts;
using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.Audit.Application.Entries.CheckAuditIntegrity;

/// <summary>
/// The Auditor's chain integrity check (AUD-05): recompute the whole chain and report either
/// <c>intact</c> with the number of entries checked, or <c>broken</c> with the first position that
/// fails. It has no parameters: the check always covers the whole record.
/// </summary>
public sealed record CheckAuditIntegrityQuery : IQuery<AuditIntegrityReport>;
