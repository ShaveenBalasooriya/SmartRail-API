namespace SmartRail.LiveOperations.Domain.Departures;

/// <summary>
/// The optional reason a Guard gives for a late departure (GRD-03). The list is fixed and records
/// service impact only; a departure may be logged without a cause. The values are stored as text by
/// <see cref="DelayCauses"/>.
/// </summary>
public enum DelayCause
{
    /// <summary>Signals, points or other signalling equipment delayed the train.</summary>
    Signalling,

    /// <summary>The train, its rolling stock or its equipment delayed the departure.</summary>
    RollingStock,

    /// <summary>The crew was not ready or not in place.</summary>
    Crew,

    /// <summary>Passengers took longer than planned to board or alight.</summary>
    PassengerBoarding,

    /// <summary>Track, station or other infrastructure work delayed the train.</summary>
    Infrastructure,

    /// <summary>Weather conditions delayed the train.</summary>
    Weather,

    /// <summary>Any other cause the list does not name.</summary>
    Other,
}
