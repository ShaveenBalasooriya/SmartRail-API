using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Domain.LiveState;

/// <summary>The named failures of creating or applying a journey's live state.</summary>
public static class JourneyLiveStateErrors
{
    /// <summary>A journey's live state cannot be created without its journey identifier.</summary>
    public static readonly Error MissingJourneyId = Error.Validation(
        "LiveState.MissingJourneyId",
        "A journey identifier is required.");

    /// <summary>A journey's live state cannot be created without its service identifier.</summary>
    public static readonly Error MissingServiceId = Error.Validation(
        "LiveState.MissingServiceId",
        "A service identifier is required.");

    /// <summary>A journey's live state cannot be created without its line identifier.</summary>
    public static readonly Error MissingLineId = Error.Validation(
        "LiveState.MissingLineId",
        "A line identifier is required.");

    /// <summary>A journey's live state cannot be created without at least one baseline stop.</summary>
    public static readonly Error MissingStops = Error.Validation(
        "LiveState.MissingStops",
        "A journey must have at least one baseline stop.");

    /// <summary>The journey has no live state, so no fact can be applied to it.</summary>
    public static Error NotFound(Guid journeyId) => Error.NotFound(
        "LiveState.NotFound",
        $"Journey '{journeyId}' has no live state.");
}
