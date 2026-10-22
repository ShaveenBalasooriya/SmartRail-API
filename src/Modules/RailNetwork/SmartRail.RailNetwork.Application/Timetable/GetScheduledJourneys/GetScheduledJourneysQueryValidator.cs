using FluentValidation;
using SmartRail.RailNetwork.Domain.Abstractions;

namespace SmartRail.RailNetwork.Application.Timetable.GetScheduledJourneys;

/// <summary>
/// The date must be today or tomorrow in the railway's time zone; a missing date defaults to today
/// in the handler (journey-generation spec). The error is keyed <c>date</c>.
/// </summary>
public sealed class GetScheduledJourneysQueryValidator : AbstractValidator<GetScheduledJourneysQuery>
{
    public GetScheduledJourneysQueryValidator(IRailwayClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        RuleFor(query => query.Date)
            .Must(date => date is null || date == clock.Today || date == clock.Tomorrow)
            .WithMessage("The date must be today or tomorrow in the railway's time zone.")
            .OverridePropertyName("date");
    }
}
