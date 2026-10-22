using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>The expected failures of creating a <see cref="Line"/>.</summary>
public static class LineErrors
{
    public static readonly Error IdRequired = Error.Validation("line.id", "A line identifier is required.");

    public static readonly Error NameRequired = Error.Validation("line.name", "A line name is required.");
}
