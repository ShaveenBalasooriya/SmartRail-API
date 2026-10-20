using FluentValidation;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.RailNetwork.Application.Network.GetLines;

/// <summary>
/// A blank direction means outbound; otherwise only outbound or inbound is accepted, ignoring case
/// (design D3, module-architecture spec). The error is keyed <c>direction</c>.
/// </summary>
public sealed class GetLinesQueryValidator : AbstractValidator<GetLinesQuery>
{
    public GetLinesQueryValidator()
    {
        RuleFor(query => query.Direction)
            .Must(BeAKnownDirection)
            .WithMessage("The direction must be outbound or inbound.")
            .OverridePropertyName("direction");
    }

    private static bool BeAKnownDirection(string? direction) =>
        string.IsNullOrWhiteSpace(direction)
        || string.Equals(direction, nameof(ServiceDirection.Outbound), StringComparison.OrdinalIgnoreCase)
        || string.Equals(direction, nameof(ServiceDirection.Inbound), StringComparison.OrdinalIgnoreCase);
}
