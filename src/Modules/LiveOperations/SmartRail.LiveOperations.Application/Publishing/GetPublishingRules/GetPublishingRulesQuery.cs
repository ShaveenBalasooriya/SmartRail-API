using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.LiveOperations.Application.Publishing.GetPublishingRules;

/// <summary>The limits a controller publishes within, read from the domain's one rule.</summary>
public sealed record GetPublishingRulesQuery : IQuery<PublishingRulesResponse>;
