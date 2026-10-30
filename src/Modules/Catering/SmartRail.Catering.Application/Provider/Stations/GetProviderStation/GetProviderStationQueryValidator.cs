using FluentValidation;

namespace SmartRail.Catering.Application.Provider.Stations.GetProviderStation;

/// <summary>
/// The station identifier is required. An empty value means none was supplied, and the error is
/// keyed by the route parameter name (provider-data-api spec, "Stations and platforms").
/// </summary>
public sealed class GetProviderStationQueryValidator : AbstractValidator<GetProviderStationQuery>
{
    public GetProviderStationQueryValidator()
    {
        RuleFor(query => query.StationId)
            .NotEmpty()
            .WithMessage("The station identifier is required.")
            .OverridePropertyName("id");
    }
}
