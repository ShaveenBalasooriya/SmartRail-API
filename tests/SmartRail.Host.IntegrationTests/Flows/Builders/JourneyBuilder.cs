using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.Flows.Builders;

/// <summary>One journey a flow created, so every later step names it instead of carrying an id about.</summary>
/// <param name="Id">The journey's identifier.</param>
/// <param name="ServiceDate">The railway day it runs on.</param>
internal sealed record Journey(Guid Id, DateOnly ServiceDate);

/// <summary>
/// Creates a journey's live state the way generation does: through the real
/// <see cref="SmartRail.RailNetwork.Contracts.JourneyGenerated"/> announcement, published into the network
/// module's outbox, which the Live Operations consumer turns into the journey's live state. The
/// journey is unique to the flow that asked for it — its own identifiers, its own stops — so no flow
/// depends on another's data.
/// </summary>
/// <remarks>
/// The stops are three seeded calling stops of the Coastal Line, the first departing an hour after the
/// frozen clock: a scheduled journey that may be delayed, cancelled, moved to another platform of
/// Colombo Fort, made running by a departure at stop 1, or withdrawn after a published delay.
/// </remarks>
internal sealed class JourneyBuilder
{
    private readonly List<Journey> _created = [];
    private readonly SmartRailApiFactory _factory;
    private readonly PostgresFixture _postgres;
    private readonly DateOnly _serviceDate;
    private readonly DateTimeOffset _now;

    public JourneyBuilder(
        SmartRailApiFactory factory,
        PostgresFixture postgres,
        DateOnly serviceDate,
        DateTimeOffset now)
    {
        _factory = factory;
        _postgres = postgres;
        _serviceDate = serviceDate;
        _now = now;
    }

    /// <summary>Every journey this flow created, which the flow removes again when it finishes.</summary>
    public IReadOnlyList<Journey> Created => _created;

    /// <summary>
    /// Creates one scheduled journey assigned to <paramref name="guardStaffId"/> and waits until its
    /// live state exists. The default guard is the seeded one, and the default shape is the three-stop
    /// journey every flow shares.
    /// </summary>
    public async Task<Journey> CreateAsync(
        Guid? guardStaffId = null,
        int stopCount = 3,
        Guid? serviceId = null,
        DateTimeOffset? firstDeparture = null)
    {
        var journey = new Journey(Guid.CreateVersion7(), _serviceDate);

        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredKeyedService<IIntegrationEventPublisher>(
            IntegrationEventPublisherKeys.Network);
        var database = scope.ServiceProvider.GetRequiredService<RailNetworkDbContext>();

        publisher.Publish(
            new JourneyGenerated(
                journey.Id,
                serviceId ?? Guid.CreateVersion7(),
                _serviceDate,
                SeededNetwork.CoastalLine,
                ServiceDirection.Outbound,
                guardStaffId ?? StaffDirectory.GuardId,
                Stops(stopCount, firstDeparture)),
            journey.Id);

        await database.SaveChangesAsync();

        await Eventually.ValueAsync(
            () => CountLiveStatesAsync(journey.Id),
            count => count == 1,
            $"the journey {journey.Id} should have its live state after generation was announced");

        _created.Add(journey);

        return journey;
    }

    /// <summary>
    /// Three calling stops, ten minutes apart, from an hour after the frozen clock; a four-stop
    /// journey puts Bambalapitiya between Kollupitiya and Mount Lavinia, so a delay published at stop 3
    /// has a later stop to reach. When <paramref name="firstDeparture"/> is given the journey starts
    /// there instead, ten minutes apart, so a flow can build a journey whose first stop was due just
    /// before the frozen clock and record its departure on time.
    /// </summary>
    private List<JourneyStopDto> Stops(int stopCount, DateTimeOffset? firstDeparture) =>
        firstDeparture is { } departure
            ? OnTimeStops(departure)
            : stopCount == 4
                ? FourStops()
                : ThreeStops();

    private static List<JourneyStopDto> OnTimeStops(DateTimeOffset departure) =>
    [
        new JourneyStopDto(
            1,
            SeededNetwork.ColomboFort,
            true,
            null,
            departure,
            SeededNetwork.ColomboFortPlatformTwo),
        new JourneyStopDto(
            2,
            SeededNetwork.Kollupitiya,
            true,
            departure.AddMinutes(10),
            departure.AddMinutes(11),
            SeededNetwork.KollupitiyaPlatform),
        new JourneyStopDto(
            3,
            SeededNetwork.Bambalapitiya,
            true,
            departure.AddMinutes(20),
            departure.AddMinutes(21),
            SeededNetwork.BambalapitiyaPlatform),
        new JourneyStopDto(
            4,
            SeededNetwork.MountLavinia,
            true,
            departure.AddMinutes(30),
            null,
            SeededNetwork.MountLaviniaPlatform),
    ];

    private List<JourneyStopDto> ThreeStops() =>
    [
        new JourneyStopDto(
            1,
            SeededNetwork.ColomboFort,
            true,
            null,
            _now.AddMinutes(60),
            SeededNetwork.ColomboFortPlatformTwo),
        new JourneyStopDto(
            2,
            SeededNetwork.Kollupitiya,
            true,
            _now.AddMinutes(70),
            _now.AddMinutes(71),
            SeededNetwork.KollupitiyaPlatform),
        new JourneyStopDto(
            3,
            SeededNetwork.MountLavinia,
            true,
            _now.AddMinutes(90),
            null,
            SeededNetwork.MountLaviniaPlatform),
    ];

    private List<JourneyStopDto> FourStops() =>
    [
        new JourneyStopDto(
            1,
            SeededNetwork.ColomboFort,
            true,
            null,
            _now.AddMinutes(60),
            SeededNetwork.ColomboFortPlatformTwo),
        new JourneyStopDto(
            2,
            SeededNetwork.Kollupitiya,
            true,
            _now.AddMinutes(70),
            _now.AddMinutes(71),
            SeededNetwork.KollupitiyaPlatform),
        new JourneyStopDto(
            3,
            SeededNetwork.Bambalapitiya,
            true,
            _now.AddMinutes(80),
            _now.AddMinutes(81),
            SeededNetwork.BambalapitiyaPlatform),
        new JourneyStopDto(
            4,
            SeededNetwork.MountLavinia,
            true,
            _now.AddMinutes(90),
            null,
            SeededNetwork.MountLaviniaPlatform),
    ];

    private async Task<long> CountLiveStatesAsync(Guid journeyId) =>
        Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            reader => reader.GetInt64(0)));
}
