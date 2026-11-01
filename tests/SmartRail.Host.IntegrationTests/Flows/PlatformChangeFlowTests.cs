using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows;

/// <summary>
/// Flow 3, a platform change everywhere, at flow level: the controller moves a calling stop to another
/// platform of its station and the passenger detail shows the new platform with the time it became known
/// and the automatically-published label; the new platform's board lists the journey and the old board
/// announces where it went; and the provider receives <c>platform_changed</c> with the new number. The
/// negatives are the route's own: an unknown platform is a bad request, and moving a stop to the platform
/// it already uses is a conflict, each leaving the journey exactly as it was.
/// </summary>
/// <remarks>
/// Every test builds its own journey through the real generation announcement and publishes through the
/// real controller route on the Host's frozen clock, so the change's time is exact. The display and the
/// detail are read for a journey that calls at the interchange, whose two platforms are the seeded pair a
/// change moves a stop between.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PlatformChangeFlowTests : IAsyncLifetime
{
    private static readonly Dictionary<string, string> ProviderSettings = new()
    {
        ["Catering:ProviderApiKey"] = Drivers.ProviderDriver.ApiKey,
        ["Catering:Webhook:PollIntervalSeconds"] = "1",
        ["Catering:Webhook:RetryDelaysSeconds"] = "2,2,2,2,2",
        ["Catering:Webhook:RequestTimeoutSeconds"] = "5",
    };

    private readonly PostgresFixture _postgres;
    private FlowContext _flow = null!;

    public PlatformChangeFlowTests(PostgresFixture postgres) => _postgres = postgres;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _flow = new FlowContext(_postgres, settings: ProviderSettings);
        return _flow.InitializeAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _flow.DisposeAsync();

    [FlowFact]
    public async Task Happy_path_a_platform_change_reaches_the_detail_both_boards_and_the_provider()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync(serviceId: SeededNetwork.Coastal1210);
        await using var provider = await ProviderReceiver.StartAsync();
        await _flow.Provider.RegisterWebhookAsync(provider.CallbackUrl);

        var before = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(before, "The public journey detail should be readable before the platform change");

        var beforeNewBoard = await _flow.Public.DisplayAsync(SeededNetwork.ColomboFort, "3");
        Verify.Ok(beforeNewBoard, "The new platform's display feed should be readable before the change");
        Verify.True(
            beforeNewBoard.Value!.ForJourney(journey.Id) is null,
            "The new platform's display feed should not list the journey before the change");

        // Act
        var change = await _flow.Changes.PlatformChangeAsync(
            journey,
            stopSequence: 1,
            SeededNetwork.ColomboFortPlatformThree);

        // Assert — the passenger's detail carries the new platform, its time and its provenance.
        var detail = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(detail, "The public journey detail should be readable after the platform change");

        var moved = detail.Value!.Stop(1);
        Verify.Equal(
            "3",
            moved.PlatformNumber,
            "The public journey detail should show stop 1 on the platform the controller moved it to");
        Verify.Equal(
            change.PublishedAt,
            moved.PlatformChangedAt,
            "The public journey detail should show when the platform change became known");
        Verify.Equal(
            "automatically_published",
            moved.Provenance,
            "The moved stop should carry the automatically-published label");
        Verify.Equal(
            "automatically_published",
            detail.Value.Provenance,
            "The journey should carry the automatically-published label while the change is in force");
        Verify.Equal(
            before.Value!.Stop(2).PlatformId,
            detail.Value.Stop(2).PlatformId,
            "The platform change should leave every other stop on its own platform");

        // Assert — the new platform's board lists the journey, flagged with the change's time.
        var newPlatform = await _flow.Public.DisplayAsync(SeededNetwork.ColomboFort, "3");
        Verify.Ok(newPlatform, "The new platform's display feed should be readable after the platform change");

        var listed = newPlatform.Value!.ForJourney(journey.Id);
        Verify.True(
            listed is not null,
            "The new platform's display feed should list the journey that moved to it");
        Verify.Equal(
            "3",
            listed!.Origin.PlatformNumber,
            "The new platform's display feed should show the journey's origin on the new platform");
        Verify.Equal(
            change.PublishedAt,
            listed.Origin.PlatformChangedAt,
            "The new platform's display feed should flag the journey with the time the change became known");
        Verify.True(
            newPlatform.Value.NoticeFor(journey.Id) is null,
            "The new platform's display feed should not announce the journey as moved away from it");

        // Assert — the old platform's board no longer lists it, and says where it went.
        var oldPlatform = await _flow.Public.DisplayAsync(SeededNetwork.ColomboFort, "2");
        Verify.Ok(oldPlatform, "The old platform's display feed should be readable after the platform change");

        Verify.True(
            oldPlatform.Value!.ForJourney(journey.Id) is null,
            "The old platform's display feed should no longer list the journey as a departure");
        var notice = oldPlatform.Value.NoticeFor(journey.Id);
        Verify.True(
            notice is not null,
            "The old platform's display feed should announce where the journey went");
        Verify.Equal(
            "3",
            notice!.NewPlatformNumber,
            "The old platform's notice should name the platform the journey moved to");

        // Assert — the provider is the flow's fourth channel: one delivery with the new number.
        await provider.WaitForDeliveriesAsync(
            1,
            "the provider should receive the platform change published on the journey");
        await provider.WaitQuietAsync(
            TimeSpan.FromSeconds(2),
            expectedCount: 1,
            "the provider should receive the published platform change exactly once");

        var delivery = Assert.Single(provider.Deliveries);

        Verify.Equal(
            "platform_changed",
            delivery.Type,
            "The provider's delivery should be a platform_changed event");
        Verify.Equal(journey.Id, delivery.JourneyId, "The provider's delivery should carry the journey that moved");
        Verify.Equal(1, delivery.StopSequence, "The provider's delivery should name the stop that moved");
        Verify.Equal(
            SeededNetwork.ColomboFortPlatformThree.ToString(),
            delivery.Values["platformId"],
            "The provider's delivery should carry the platform the stop moved to");
        Verify.Equal(
            "3",
            delivery.Values["platformNumber"],
            "The provider's delivery should carry the number of the platform the stop moved to");
    }

    [FlowFact]
    public async Task An_unknown_platform_is_a_bad_request_and_changes_nothing()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync();
        var before = await _flow.Public.JourneyAsync(journey.Id);
        Verify.Ok(before, "The public journey detail should be readable before the refused change");

        // Act
        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishPlatformChangeAsync(journey.Id, stopSequence: 1, Guid.CreateVersion7());

        // Assert
        Verify.BadRequest(attempt, "A platform change naming a platform the network does not have should be refused");

        var after = await _flow.Public.JourneyAsync(journey.Id);
        Verify.Ok(after, "The public journey detail should still be readable after the refusal");
        Verify.Unchanged(
            before.Value!.RawJson,
            after.Value!.RawJson,
            "A refused platform change naming an unknown platform should leave the public detail unchanged");
    }

    [FlowFact]
    public async Task Publishing_the_platform_the_stop_already_uses_is_a_conflict_and_changes_nothing()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync();
        var before = await _flow.Public.JourneyAsync(journey.Id);
        Verify.Ok(before, "The public journey detail should be readable before the refused change");

        // Act
        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishPlatformChangeAsync(
                journey.Id,
                stopSequence: 1,
                SeededNetwork.ColomboFortPlatformTwo);

        // Assert
        Verify.Conflict(
            attempt,
            "A platform change to the platform stop 1 already uses should be refused as a conflict");

        var after = await _flow.Public.JourneyAsync(journey.Id);
        Verify.Ok(after, "The public journey detail should still be readable after the refusal");
        Verify.Unchanged(
            before.Value!.RawJson,
            after.Value!.RawJson,
            "A refused platform change to the current platform should leave the public detail unchanged");
    }
}
