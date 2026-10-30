using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Flows;

/// <summary>
/// Flow 3, a platform change everywhere, at flow level: the controller moves a calling stop to another
/// platform of its station and the passenger detail shows the new platform with the time it became
/// known and the automatically-published label; the station display lists the journey on the new
/// platform and no longer as a departure of the old one; the Guard's view shows the same platform; and
/// the public stream delivers the platform-changed event. The negatives are the route's own: an unknown
/// platform, a stop the journey does not have, the platform the stop already uses, and a Guard's or an
/// Auditor's attempt are each refused and leave the journey as it was. The provider is the fifth
/// channel: the flow checks the platform_changed delivery a stub provider receiver accepts.
/// </summary>
/// <remarks>
/// Every test builds its own journey through the real generation announcement and publishes through the
/// real controller route on the Host's frozen clock, so the change's time is exact. The display and the
/// detail are read for a journey that calls at the interchange, whose two platforms are the seeded pair
/// a change moves a stop between.
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

    [Fact]
    public async Task The_public_journey_detail_shows_the_new_platform_with_its_time_and_provenance()
    {
        var journey = await _flow.Journeys.CreateAsync();
        var before = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(before, "The public journey detail should be readable before the platform change");

        var change = await _flow.Changes.PlatformChangeAsync(
            journey,
            stopSequence: 1,
            SeededNetwork.ColomboFortPlatformThree);

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
    }

    [Fact]
    public async Task The_display_feed_moves_the_journey_to_the_new_platform_and_off_the_old_one()
    {
        var journey = await _flow.Journeys.CreateAsync(serviceId: SeededNetwork.Coastal1210);

        var change = await _flow.Changes.PlatformChangeAsync(
            journey,
            stopSequence: 1,
            SeededNetwork.ColomboFortPlatformThree);

        var newPlatform = await _flow.Public.DisplayAsync(SeededNetwork.ColomboFort, "3");
        Verify.Ok(newPlatform, "The new platform's display feed should be readable after the platform change");

        var moved = newPlatform.Value!.ForJourney(journey.Id);
        Verify.True(
            moved is not null,
            "The new platform's display feed should list the journey that moved to it");
        Verify.Equal(
            "3",
            moved!.Origin.PlatformNumber,
            "The new platform's display feed should show the journey's origin on the new platform");
        Verify.Equal(
            change.PublishedAt,
            moved.Origin.PlatformChangedAt,
            "The new platform's display feed should flag the journey with the time the change became known");
        Verify.True(
            newPlatform.Value.NoticeFor(journey.Id) is null,
            "The new platform's display feed should not announce the journey as moved away from it");

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
    }

    [Fact]
    public async Task The_guard_view_shows_the_platform_the_controller_published()
    {
        var journey = await _flow.Journeys.CreateAsync();

        var change = await _flow.Changes.PlatformChangeAsync(
            journey,
            stopSequence: 1,
            SeededNetwork.ColomboFortPlatformThree);

        var guard = await _flow.Guard.JourneyViewAsync(journey.Id);
        Verify.Ok(guard, "The Guard's view should be readable for the journey assigned to the guard");

        var stop = guard.Value!.Stop(1);
        Verify.Equal(
            "3",
            stop.PlatformNumber,
            "The Guard's view should show stop 1 on the platform the controller moved it to");
        Verify.Equal(
            change.PublishedAt,
            stop.PlatformChangedAt,
            "The Guard's view should show when the platform change became known");
        Verify.Equal(
            1,
            guard.Value.Changes.Count,
            "The Guard's view should hold the one platform change published on the journey");
        Verify.Equal(
            "platform_change",
            guard.Value.Changes[0].Type,
            "The Guard's view should hold the platform change as its own kind of change");
    }

    [Fact]
    public async Task The_public_stream_delivers_a_platform_changed_event_with_the_new_platform()
    {
        var journey = await _flow.Journeys.CreateAsync();

        using var stream = await _flow.Public.OpenStreamAsync(journey.Id);

        await _flow.Changes.PlatformChangeAsync(
            journey,
            stopSequence: 1,
            SeededNetwork.ColomboFortPlatformThree);

        var delivered = await stream.ReadAsync(Eventually.Timeout, stopAtFirstEvent: true);

        Verify.Equal(
            1,
            delivered.Events.Count,
            "The public stream should deliver the platform change as exactly one event");
        Verify.Equal(
            "platform_changed",
            delivered.Events[0].EventType,
            "The public stream should deliver the platform change as a platform_changed event");

        using var payload = System.Text.Json.JsonDocument.Parse(delivered.Events[0].Data);
        var stop = payload.RootElement.GetProperty("stops").EnumerateArray()
            .Single(candidate => candidate.GetProperty("sequence").GetInt32() == 1);

        Verify.Equal(
            "3",
            stop.GetProperty("platform").GetProperty("number").GetString(),
            "The stream's platform_changed event should carry the new platform for the moved stop");
    }

    [Fact]
    public async Task An_unknown_platform_is_refused_and_changes_nothing()
    {
        var journey = await _flow.Journeys.CreateAsync();
        var before = await _flow.Internals.SnapshotAsync(journey.Id);

        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishPlatformChangeAsync(journey.Id, stopSequence: 1, Guid.CreateVersion7());

        Verify.BadRequest(attempt, "A platform change naming a platform the network does not have should be refused");

        var after = await _flow.Internals.SnapshotAsync(journey.Id);
        Verify.Unchanged(
            before.Render(),
            after.Render(),
            "A refused platform change naming an unknown platform should publish no change, event or outbox row");
    }

    [Fact]
    public async Task A_stop_the_journey_does_not_have_is_refused_and_changes_nothing()
    {
        var journey = await _flow.Journeys.CreateAsync();
        var before = await _flow.Internals.SnapshotAsync(journey.Id);

        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishPlatformChangeAsync(journey.Id, stopSequence: 9, SeededNetwork.ColomboFortPlatformThree);

        Verify.BadRequest(attempt, "A platform change at a stop sequence the journey does not have should be refused");

        var after = await _flow.Internals.SnapshotAsync(journey.Id);
        Verify.Unchanged(
            before.Render(),
            after.Render(),
            "A refused platform change at an unknown stop should publish no change, event or outbox row");
    }

    [Fact]
    public async Task Publishing_the_platform_the_stop_already_uses_is_refused_as_a_conflict()
    {
        var journey = await _flow.Journeys.CreateAsync();
        var before = await _flow.Internals.SnapshotAsync(journey.Id);

        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishPlatformChangeAsync(
                journey.Id,
                stopSequence: 1,
                SeededNetwork.ColomboFortPlatformTwo);

        Verify.Conflict(
            attempt,
            "A platform change to the platform stop 1 already uses should be refused as a conflict");

        var after = await _flow.Internals.SnapshotAsync(journey.Id);
        Verify.Unchanged(
            before.Render(),
            after.Render(),
            "A refused platform change to the current platform should publish no change, event or outbox row");
    }

    [Theory]
    [InlineData(Roles.Guard)]
    [InlineData(Roles.Auditor)]
    public async Task A_guard_or_an_auditor_cannot_change_a_platform_and_the_journey_is_unchanged(string role)
    {
        var session = role == Roles.Guard ? StaffDirectory.Guard : StaffDirectory.Auditor;
        var journey = await _flow.Journeys.CreateAsync();
        var before = await _flow.Internals.SnapshotAsync(journey.Id);
        var beforeDetail = await _flow.Public.JourneyViewAsync(journey.Id);
        var baseline = await _flow.Identity.MaxPositionAsync();

        var refused = await _flow.ControllerWrites(session).PublishPlatformChangeAsync(
            journey.Id,
            stopSequence: 1,
            SeededNetwork.ColomboFortPlatformThree);

        Verify.Refused(refused, $"A {role}'s platform change should be refused");

        await _flow.Identity.WaitUntilDeliveredAsync(
            baseline,
            $"a {role}'s refused platform change should be announced and delivered to the audit consumer");

        var after = await _flow.Internals.SnapshotAsync(journey.Id);
        Verify.Unchanged(
            before.Render(),
            after.Render(),
            $"A {role}'s refused platform change should leave the journey's changes, events and outbox rows untouched");

        var afterDetail = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(afterDetail, $"The public journey detail should still be readable after a {role}'s refusal");
        Verify.Equal(
            beforeDetail.Value!.Stop(1).PlatformNumber,
            afterDetail.Value!.Stop(1).PlatformNumber,
            $"A {role}'s refused platform change should leave the stop on the platform it had");
    }

    /// <summary>
    /// The provider is the fifth channel of the flow: the subscription, the dispatcher and the delivery
    /// row are real, and only the receiving server is a stub.
    /// </summary>
    [Fact]
    public async Task The_provider_receives_the_platform_change_with_the_new_platform_number()
    {
        var journey = await _flow.Journeys.CreateAsync();
        await using var provider = await ProviderReceiver.StartAsync();

        await _flow.Provider.RegisterWebhookAsync(provider.CallbackUrl);

        await _flow.Changes.PlatformChangeAsync(
            journey,
            stopSequence: 1,
            SeededNetwork.ColomboFortPlatformThree);

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

        await _flow.Provider.WaitForDeliveredAsync(
            journey.Id,
            expected: 1,
            "the platform change's delivery should be marked delivered once the provider accepted it");
    }
}
