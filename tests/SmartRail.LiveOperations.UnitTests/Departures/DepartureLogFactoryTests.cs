using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.UnitTests.Departures;

/// <summary>
/// The shape rules of a departure log: the client-generated identifier, the journey, stop, station
/// and Guard are required, and an explicitly given delay cause has to be one of the seven values.
/// The rules against the journey (calling stop, terminus, timestamps, time order, service date) are
/// covered by <c>DepartureLogRulesTests</c> and <c>JourneyLiveStateDepartureRulesTests</c>.
/// </summary>
public sealed class DepartureLogFactoryTests
{
    private static readonly DateTimeOffset DepartedAt = new(2026, 10, 28, 8, 2, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 28, 8, 25, 0, TimeSpan.Zero);

    [Fact]
    public void A_log_carries_the_device_values_and_the_separate_receipt_time()
    {
        var id = Guid.CreateVersion7();
        var journeyId = Guid.CreateVersion7();
        var stationId = Guid.CreateVersion7();
        var guardId = Guid.CreateVersion7();

        var result = DepartureLog.Create(
            id,
            journeyId,
            stopSequence: 2,
            stationId,
            guardId,
            DepartedAt,
            DelayCause.Crew,
            ReceivedAt);

        Assert.True(result.IsSuccess);
        var log = result.Value;
        Assert.Equal(id, log.Id);
        Assert.Equal(journeyId, log.JourneyId);
        Assert.Equal(2, log.StopSequence);
        Assert.Equal(stationId, log.StationId);
        Assert.Equal(guardId, log.GuardStaffId);
        Assert.Equal(DepartedAt, log.DepartedAt);
        Assert.Equal(DelayCause.Crew, log.DelayCause);
        Assert.Equal(ReceivedAt, log.ReceivedAt);
        Assert.Equal(0, log.FactOrder);
    }

    [Fact]
    public void A_log_may_omit_the_delay_cause()
    {
        var result = Create(delayCause: null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.DelayCause);
    }

    [Fact]
    public void A_log_needs_a_client_generated_identifier()
    {
        var result = Create(id: Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.MissingId, result.Error);
    }

    [Fact]
    public void A_log_needs_its_journey()
    {
        var result = Create(journeyId: Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.MissingJourneyId, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_log_needs_a_stop_sequence_starting_at_one(int stopSequence)
    {
        var result = Create(stopSequence: stopSequence);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.InvalidStopSequence, result.Error);
    }

    [Fact]
    public void A_log_needs_the_station_of_its_stop()
    {
        var result = Create(stationId: Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.MissingStationId, result.Error);
    }

    [Fact]
    public void A_log_needs_the_guard_who_recorded_it()
    {
        var result = Create(guardStaffId: Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.MissingGuard, result.Error);
    }

    [Fact]
    public void A_log_needs_the_device_time_of_the_tap()
    {
        var result = DepartureLog.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            stopSequence: 2,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            departedAt: default,
            delayCause: null,
            ReceivedAt);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.MissingDepartedAt, result.Error);
    }

    [Fact]
    public void A_log_needs_the_server_time_it_was_received()
    {
        var result = DepartureLog.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            stopSequence: 2,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DepartedAt,
            delayCause: null,
            receivedAt: default);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.MissingReceivedAt, result.Error);
    }

    [Fact]
    public void A_log_refuses_a_cause_outside_the_fixed_list()
    {
        var result = Create(delayCause: (DelayCause)99);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.InvalidDelayCause, result.Error);
    }

    private static Result<DepartureLog> Create(
        Guid? id = null,
        Guid? journeyId = null,
        int stopSequence = 2,
        Guid? stationId = null,
        Guid? guardStaffId = null,
        DateTimeOffset? departedAt = null,
        DelayCause? delayCause = null,
        DateTimeOffset? receivedAt = null) =>
        DepartureLog.Create(
            id ?? Guid.CreateVersion7(),
            journeyId ?? Guid.CreateVersion7(),
            stopSequence,
            stationId ?? Guid.CreateVersion7(),
            guardStaffId ?? Guid.CreateVersion7(),
            departedAt ?? DepartedAt,
            delayCause,
            receivedAt ?? ReceivedAt);
}
