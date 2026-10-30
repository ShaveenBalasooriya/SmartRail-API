using FluentValidation;

namespace SmartRail.Distribution.Application.Public.Departures.GetStationDepartures;

/// <summary>
/// A missing window means the default two hours; any value the caller sends must be a whole number
/// of minutes from one to six hours (passenger-departures spec). The error is keyed
/// <c>windowMinutes</c>.
/// </summary>
public sealed class GetStationDeparturesQueryValidator : AbstractValidator<GetStationDeparturesQuery>
{
    public GetStationDeparturesQueryValidator()
    {
        RuleFor(query => query.WindowMinutes)
            .Must(windowMinutes => windowMinutes is null
                || (windowMinutes >= 1 && windowMinutes <= PublicDepartureSelection.MaximumWindowMinutes))
            .WithMessage(
                $"The window must be between 1 and {PublicDepartureSelection.MaximumWindowMinutes} minutes.")
            .OverridePropertyName("windowMinutes");
    }
}
