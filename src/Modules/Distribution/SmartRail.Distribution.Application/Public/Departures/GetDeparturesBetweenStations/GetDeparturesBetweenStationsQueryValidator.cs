using FluentValidation;

namespace SmartRail.Distribution.Application.Public.Departures.GetDeparturesBetweenStations;

/// <summary>
/// Both stations are required. An empty value means one was missing or not a station identifier,
/// and the error is keyed by the query parameter name (passenger-departures spec).
/// </summary>
public sealed class GetDeparturesBetweenStationsQueryValidator
    : AbstractValidator<GetDeparturesBetweenStationsQuery>
{
    public GetDeparturesBetweenStationsQueryValidator()
    {
        RuleFor(query => query.From)
            .NotEmpty()
            .WithMessage("The origin station is required.")
            .OverridePropertyName("from");

        RuleFor(query => query.To)
            .NotEmpty()
            .WithMessage("The destination station is required.")
            .OverridePropertyName("to");
    }
}
