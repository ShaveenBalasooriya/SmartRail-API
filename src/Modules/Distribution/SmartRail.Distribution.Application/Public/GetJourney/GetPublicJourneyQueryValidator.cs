using FluentValidation;

namespace SmartRail.Distribution.Application.Public.GetJourney;

/// <summary>
/// The journey identifier is required. An empty value means none was supplied, and the error is
/// keyed by the route parameter name (passenger-departures spec, journey detail).
/// </summary>
public sealed class GetPublicJourneyQueryValidator : AbstractValidator<GetPublicJourneyQuery>
{
    public GetPublicJourneyQueryValidator()
    {
        RuleFor(query => query.JourneyId)
            .NotEmpty()
            .WithMessage("The journey identifier is required.")
            .OverridePropertyName("id");
    }
}
