using System.Net;
using SmartRail.Host.Http;
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
/// The announcement is the one in-process step a flow keeps: no public route generates a journey, so a
/// flow that wants exact times builds its own. Everything after it — the live state appearing, the
/// times, the channels — is read back through the public API.
/// </remarks>
internal sealed class JourneyBuilder
{
    private readonly List<Journey> _created = [];
    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;
    private readonly DateOnly _serviceDate;
    private readonly DateTimeOffset _now;

    public JourneyBuilder(
        SmartRailApiFactory factory,
        HttpClient client,
        DateOnly serviceDate,
        DateTimeOffset now)
    {
        _factory = factory;
        _client = client;
        _serviceDate = serviceDate;
        _now = now;
    }

    /// <summary>Every journey this flow created, which the flow removes again when it finishes.</summary>
    public IReadOnlyList<Journey> Created => _created;

    /// <summary>
    /// Creates one scheduled journey assigned to <paramref name="guardStaffId"/> and waits until its
    /// live state is public. The default guard is the seeded one, and the default shape is the
    /// three-stop journey every flow shares.
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
            () => IsPublishedAsync(journey.Id),
            published => published,
            $"journey {journey.Id} should become readable through the public detail once generation was announced");

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

    /// <summary>
    /// Whether the public detail answers for the journey yet. Generating a journey is announced through
    /// the module's outbox, so the live state appears just after the announcement commits; the public
    /// detail is the earliest public evidence that it has.
    /// </summary>
    private async Task<bool> IsPublishedAsync(Guid journeyId)
    {
        using var response = await _client.GetAsync(
            $"{ApiRoutes.BasePath}/public/journeys/{journeyId}");

        return response.StatusCode == HttpStatusCode.OK;
    }
}
