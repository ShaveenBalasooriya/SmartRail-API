using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Application.Guard;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.LiveOperations.UnitTests.Guard;

/// <summary>
/// What the guard's view shows of one journey (guard-assignments spec): only the calling stops, the
/// final stop marked as having no departure to log, the departure logged flag, the station and
/// platform names the catalogue resolves, and the published changes without their publisher.
/// </summary>
public sealed class GuardJourneyMappingTests
{
    private static readonly DateTimeOffset CreatedAt = At(7, 0);
    private static readonly Guid Alpha = Guid.CreateVersion7();
    private static readonly Guid Beta = Guid.CreateVersion7();
    private static readonly Guid Gamma = Guid.CreateVersion7();
    private static readonly Guid AlphaPlatform = Guid.CreateVersion7();
    private static readonly Guid BetaPlatform = Guid.CreateVersion7();

    [Fact]
    public void Only_the_calling_stops_are_shown_and_the_last_one_is_marked_final()
    {
        var state = Journey();
        var journey = Timetable(state);

        var response = GuardJourneyMapping.ToResponse(state, journey, Catalogue());

        Assert.Equal(new[] { 1, 3 }, response.Stops.Select(stop => stop.Sequence));
        Assert.False(response.Stops[0].IsFinalStop);
        Assert.True(response.Stops[1].IsFinalStop);
        Assert.All(response.Stops, stop => Assert.False(stop.DepartureLogged));
    }

    [Fact]
    public void A_recorded_departure_marks_its_stop_as_logged()
    {
        var state = Journey();
        var departure = At(8, 1);
        Assert.True(state.Apply([new DepartureFact(1, departure, departure, StreamOrder: 1)], departure));

        var response = GuardJourneyMapping.ToResponse(state, Timetable(state), Catalogue());

        Assert.True(response.Stops[0].DepartureLogged);
        Assert.Equal(departure, response.Stops[0].ActualDeparture);

        // The final stop has no departure to log, so it never reports one.
        Assert.False(response.Stops[1].DepartureLogged);
    }

    [Fact]
    public void Station_and_platform_names_come_from_the_network_catalogue()
    {
        var state = Journey();

        var response = GuardJourneyMapping.ToResponse(state, Timetable(state), Catalogue());

        Assert.Equal(new StationRef(Alpha, "Alpha"), response.Stops[0].Station);
        Assert.Equal(new PlatformRef(AlphaPlatform, "1"), response.Stops[0].Platform);
        Assert.Equal(new StationRef(Gamma, "Gamma"), response.Stops[1].Station);
        Assert.Null(response.Stops[1].Platform);
    }

    [Fact]
    public void The_journey_names_come_from_the_timetable_and_are_empty_without_it()
    {
        var state = Journey();

        var response = GuardJourneyMapping.ToResponse(state, journey: null, Catalogue());

        Assert.Equal(state.Id, response.Id);
        Assert.Null(response.ServiceName);
        Assert.Null(response.LineCode);
        Assert.Null(response.LineName);
        Assert.Null(response.Direction);
        Assert.Equal(LiveStateMapping.ToContract(state.Status), response.Status);
        Assert.Equal(state.DelayMinutes, response.DelayMinutes);
        Assert.Equal(state.LastUpdatedAt, response.LastUpdatedAt);
    }

    [Fact]
    public void The_detail_carries_every_published_change_without_its_publisher()
    {
        var state = Journey();
        var publishedAt = At(7, 30);
        var changeId = Guid.CreateVersion7();
        var values = new Dictionary<string, string?> { ["delayMinutes"] = "5" };

        var detail = GuardJourneyMapping.ToDetail(
            state,
            Timetable(state),
            Catalogue(),
            [
                new StatusChangeView(
                    changeId,
                    "delay",
                    values,
                    Guid.CreateVersion7(),
                    publishedAt,
                    "automatically_published",
                    Corrected: true),
            ]);

        Assert.Equal(state.Id, detail.Journey.Id);
        var change = Assert.Single(detail.Changes);
        Assert.Equal(changeId, change.Id);
        Assert.Equal("delay", change.Type);
        Assert.Equal(values, change.Values);
        Assert.Equal(publishedAt, change.PublishedAt);
        Assert.True(change.Corrected);

        // The guard does not need the publisher, so no staff identifier reaches this response.
        Assert.Equal(
            new[] { "Corrected", "Id", "PublishedAt", "Type", "Values" },
            typeof(GuardChangeResponse)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    private static JourneyLiveState Journey()
    {
        var baseline = new Baseline(
            Guid.CreateVersion7(),
            new DateOnly(2026, 10, 28),
            Guid.CreateVersion7(),
            [
                new BaselineStop(1, Alpha, true, null, At(8, 0), AlphaPlatform),
                new BaselineStop(2, Beta, false, null, null, null),
                new BaselineStop(3, Gamma, true, At(8, 20), null, null),
            ],
            CreatedAt);

        return JourneyLiveState
            .Create(baseline, Guid.CreateVersion7(), Guid.CreateVersion7(), CreatedAt)
            .Value;
    }

    private static JourneyDto Timetable(JourneyLiveState state) =>
        new(
            state.Id,
            state.ServiceId,
            "Kandy Express",
            state.LineId,
            "L1",
            "Main Line",
            ServiceDirection.Outbound,
            state.ServiceDate,
            state.GuardStaffId,
            At(8, 0),
            []);

    private static StationIndex Catalogue() =>
        new(
        [
            new StationDto(
                Alpha,
                "ALP",
                "Alpha",
                IsInterchange: false,
                [new PlatformDto(AlphaPlatform, Alpha, "1")],
                []),
            new StationDto(Beta, "BET", "Beta", IsInterchange: false, [], []),
            new StationDto(Gamma, "GAM", "Gamma", IsInterchange: false, [], []),
        ]);

    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 10, 28, hour, minute, 0, TimeSpan.Zero);
}
