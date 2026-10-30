using SmartRail.Audit.Contracts;
using SmartRail.Audit.Domain.Entries;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Audit.Application.Entries.CheckAuditIntegrity;

internal sealed class CheckAuditIntegrityQueryHandler(IAuditChainReader chain)
    : IQueryHandler<CheckAuditIntegrityQuery, AuditIntegrityReport>
{
    /// <summary>The wire value of an intact record.</summary>
    private const string IntactStatus = "intact";

    /// <summary>The wire value of a record that stopped verifying.</summary>
    private const string BrokenStatus = "broken";

    public async Task<Result<AuditIntegrityReport>> Handle(
        CheckAuditIntegrityQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var verification = await AuditChainVerifier.VerifyAsync(
            chain.ReadChainAsync(cancellationToken),
            cancellationToken);

        // The status is the record's word for the outcome, not an enumeration's number: the Auditor
        // reads "intact" or "broken", and a broken record names the position that failed (AUD-05).
        var report = new AuditIntegrityReport(
            verification.IsIntact ? IntactStatus : BrokenStatus,
            verification.FirstFailingPosition,
            verification.EntriesChecked);

        return Result.Success(report);
    }
}
