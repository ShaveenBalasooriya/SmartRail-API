using FluentValidation;

namespace SmartRail.Distribution.Application.Public.Display;

/// <summary>
/// A missing count means the default eight; any value the caller sends must be from one to twenty
/// (station-display-feed spec). The error is keyed <c>count</c>.
/// </summary>
public sealed class GetPlatformFeedQueryValidator : AbstractValidator<GetPlatformFeedQuery>
{
    public GetPlatformFeedQueryValidator()
    {
        RuleFor(query => query.Count)
            .Must(count => count is null
                || (count >= 1 && count <= PublicPlatformSelection.MaximumCount))
            .WithMessage(
                $"The count must be between 1 and {PublicPlatformSelection.MaximumCount}.")
            .OverridePropertyName("count");
    }
}
