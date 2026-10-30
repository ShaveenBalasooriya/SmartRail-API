using FluentValidation;

namespace SmartRail.Audit.Application.Entries.GetAuditEntries;

/// <summary>
/// The list's input rules: the outcome is <c>succeeded</c> or <c>denied</c>, the range's start is
/// not after its end, and a page holds at most 100 entries. The cap is tighter than the shared page
/// request's maximum of 200 because the audit-record spec names 100; the shared type is not changed
/// (implementation-plan D-4).
/// </summary>
public sealed class GetAuditEntriesQueryValidator : AbstractValidator<GetAuditEntriesQuery>
{
    /// <summary>The largest page the audit list accepts (design D6).</summary>
    private const int PageSizeLimit = 100;

    public GetAuditEntriesQueryValidator()
    {
        RuleFor(query => query.Outcome)
            .Must(BeAKnownOutcome)
            .WithMessage("The outcome must be succeeded or denied.")
            .OverridePropertyName("outcome");

        RuleFor(query => query)
            .Must(query => query.From is not { } from || query.To is not { } to || from <= to)
            .WithMessage("The start of the range must not be after its end.")
            .OverridePropertyName("from");

        RuleFor(query => query.PageSize)
            .LessThanOrEqualTo(PageSizeLimit)
            .WithMessage("The page size must be 100 or less.")
            .OverridePropertyName("pageSize");
    }

    private static bool BeAKnownOutcome(string? outcome) =>
        string.IsNullOrWhiteSpace(outcome)
        || string.Equals(outcome, "succeeded", StringComparison.OrdinalIgnoreCase)
        || string.Equals(outcome, "denied", StringComparison.OrdinalIgnoreCase);
}
