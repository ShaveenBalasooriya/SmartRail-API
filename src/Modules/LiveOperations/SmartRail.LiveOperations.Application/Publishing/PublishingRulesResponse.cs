namespace SmartRail.LiveOperations.Application.Publishing;

/// <summary>
/// The limits a controller publishes within, published so a client does not restate the rule
/// (status-publication spec). The API keeps enforcing them.
/// </summary>
/// <param name="Delay">The inclusive delay range in whole minutes.</param>
public sealed record PublishingRulesResponse(DelayRuleResponse Delay);

/// <summary>The inclusive delay range a controller may publish, in whole minutes.</summary>
/// <param name="MinMinutes">The shortest delay accepted.</param>
/// <param name="MaxMinutes">The longest delay accepted.</param>
public sealed record DelayRuleResponse(int MinMinutes, int MaxMinutes);
