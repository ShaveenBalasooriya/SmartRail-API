using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Application.Publishing.GetPublishingRules;

internal sealed class GetPublishingRulesQueryHandler
    : IQueryHandler<GetPublishingRulesQuery, PublishingRulesResponse>
{
    public Task<Result<PublishingRulesResponse>> Handle(
        GetPublishingRulesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = new PublishingRulesResponse(
            new DelayRuleResponse(StatusChange.MinDelayMinutes, StatusChange.MaxDelayMinutes));

        return Task.FromResult(Result.Success(response));
    }
}
