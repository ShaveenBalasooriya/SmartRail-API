using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Domain.Changes;

/// <summary>The expected failures of creating a <see cref="StatusChange"/>.</summary>
public static class StatusChangeErrors
{
    /// <summary>A change cannot exist without the journey it was published on.</summary>
    public static readonly Error MissingJourneyId = Error.Validation(
        "StatusChange.MissingJourneyId",
        "A journey identifier is required.");

    /// <summary>A change cannot exist without the controller who published it.</summary>
    public static readonly Error MissingPublisher = Error.Validation(
        "StatusChange.MissingPublisher",
        "The publishing controller is required.");

    /// <summary>A change cannot exist without the server time it was published.</summary>
    public static readonly Error MissingPublishedAt = Error.Validation(
        "StatusChange.MissingPublishedAt",
        "A change needs the time it was published.");

    /// <summary>A delay needs the calling stop it starts at.</summary>
    public static readonly Error DelayStopSequenceRequired = Error.Validation(
        "StatusChange.DelayStopSequenceRequired",
        "A delay needs the calling stop it starts at.");

    /// <summary>A delay is a whole number of minutes within the published range.</summary>
    public static readonly Error DelayMinutesOutOfRange = Error.Validation(
        "StatusChange.DelayMinutesOutOfRange",
        $"A delay must be between {StatusChange.MinDelayMinutes} and {StatusChange.MaxDelayMinutes} minutes.");

    /// <summary>A platform change needs the calling stop whose platform changes.</summary>
    public static readonly Error PlatformStopSequenceRequired = Error.Validation(
        "StatusChange.PlatformStopSequenceRequired",
        "A platform change needs the calling stop whose platform changes.");

    /// <summary>A platform change needs the platform the stop moves to.</summary>
    public static readonly Error NewPlatformRequired = Error.Validation(
        "StatusChange.NewPlatformRequired",
        "A platform change needs the new platform.");

    /// <summary>A recorded previous platform must be a real identifier, not the empty one.</summary>
    public static readonly Error PreviousPlatformInvalid = Error.Validation(
        "StatusChange.PreviousPlatformInvalid",
        "The previous platform cannot be the empty identifier.");

    /// <summary>A termination needs the last stop the train served.</summary>
    public static readonly Error TerminationStopSequenceRequired = Error.Validation(
        "StatusChange.TerminationStopSequenceRequired",
        "A termination needs the last stop the train served.");

    /// <summary>A correction needs the change it withdraws.</summary>
    public static readonly Error CorrectionTargetRequired = Error.Validation(
        "StatusChange.CorrectionTargetRequired",
        "A correction needs the change it corrects.");

    /// <summary>The change a correction targets has to exist (change-correction spec).</summary>
    public static readonly Error CorrectionTargetNotFound = Error.NotFound(
        "StatusChange.CorrectionTargetNotFound",
        "The change to correct does not exist.");

    /// <summary>Corrections of corrections are not supported in MVP 1 (change-correction spec).</summary>
    public static readonly Error CorrectionOfCorrection = Error.Conflict(
        "StatusChange.CorrectionOfCorrection",
        "A correction cannot itself be corrected.");

    /// <summary>A change is corrected at most once (change-correction spec).</summary>
    public static readonly Error ChangeAlreadyCorrected = Error.Conflict(
        "StatusChange.ChangeAlreadyCorrected",
        "The change has already been corrected.");

    /// <summary>Only a journey dated today in the railway time zone may be changed.</summary>
    public static readonly Error JourneyNotToday = Error.Validation(
        "StatusChange.JourneyNotToday",
        "A change can be published only on a journey dated today in the railway time zone.");

    /// <summary>Only a scheduled or running journey is still open to changes.</summary>
    public static readonly Error JourneyClosed = Error.Conflict(
        "StatusChange.JourneyClosed",
        "A change can be published only on a scheduled or running journey.");

    /// <summary>A delay may start only at a stop the journey calls at.</summary>
    public static readonly Error DelayStopNotCalling = Error.Validation(
        "StatusChange.DelayStopNotCalling",
        "A delay must start at a calling stop of the journey.");

    /// <summary>A delay may not start at a stop the Guard has already departed from.</summary>
    public static readonly Error DelayStopAlreadyDeparted = Error.Conflict(
        "StatusChange.DelayStopAlreadyDeparted",
        "A delay cannot start at a stop whose departure has already been recorded.");

    /// <summary>
    /// A journey with a recorded departure is ended as an early termination, not cancelled
    /// (status-publication spec, publish a cancellation).
    /// </summary>
    public static readonly Error CancellationAfterDeparture = Error.Conflict(
        "StatusChange.CancellationAfterDeparture",
        "A journey with a recorded departure cannot be cancelled; a running journey must be ended "
        + "as an early termination instead.");

    /// <summary>
    /// A journey that has not started is cancelled rather than ended early: there is nothing to end
    /// (status-publication spec, publish an early termination).
    /// </summary>
    public static readonly Error TerminationNotStarted = Error.Conflict(
        "StatusChange.TerminationNotStarted",
        "A journey that has not started cannot be ended early; cancel it instead.");

    /// <summary>A termination may only end at a stop the journey calls at.</summary>
    public static readonly Error TerminationStopNotCalling = Error.Validation(
        "StatusChange.TerminationStopNotCalling",
        "A termination must end at a calling stop of the journey.");

    /// <summary>A terminating journey ends before its terminus, so its final calling stop cannot serve.</summary>
    public static readonly Error TerminationAtFinalStop = Error.Validation(
        "StatusChange.TerminationAtFinalStop",
        "A termination must end before the journey's final calling stop.");

    /// <summary>A termination cannot end before a stop the Guard has already departed from.</summary>
    public static readonly Error TerminationBeforeLastDeparture = Error.Conflict(
        "StatusChange.TerminationBeforeLastDeparture",
        "A termination cannot end before the last stop whose departure has already been recorded.");

    /// <summary>A platform change may only move a stop the journey calls at.</summary>
    public static readonly Error PlatformStopNotCalling = Error.Validation(
        "StatusChange.PlatformStopNotCalling",
        "A platform change must belong to a calling stop of the journey.");

    /// <summary>A stop the Guard has already departed from keeps the platform it departed from.</summary>
    public static readonly Error PlatformStopAlreadyDeparted = Error.Conflict(
        "StatusChange.PlatformStopAlreadyDeparted",
        "A platform cannot change at a stop whose departure has already been recorded.");

    /// <summary>The new platform has to be one of the stop's station's platforms.</summary>
    public static readonly Error PlatformNotAtStation = Error.Validation(
        "StatusChange.PlatformNotAtStation",
        "The new platform must be one of the platforms of the stop's station.");

    /// <summary>A platform change has to move the stop to a different platform.</summary>
    public static readonly Error PlatformUnchanged = Error.Conflict(
        "StatusChange.PlatformUnchanged",
        "The stop already uses that platform.");
}
