using System.Text.Json;
using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Domain.Webhooks;
using SmartRail.Catering.UnitTests.TestDoubles;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Catering.UnitTests.Webhooks;

public sealed class WebhookPayloadFactoryTests
{
    [Fact]
    public async Task Happy_path_builds_the_versioned_delay_payload_from_the_event_snapshot()
    {
        // Arrange
        var journeyEvent = NewEvent(JourneyEventType.DelayPublished, delayMinutes: 15);
        var factory = new WebhookPayloadFactory(new StubNetworkCatalog());

        // Act
        var json = await factory.BuildAsync(
            WebhookEventType.DelayPublished,
            journeyEvent,
            CancellationToken.None);

        // Assert
        using var payload = JsonDocument.Parse(json);
        var root = payload.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(journeyEvent.Id, root.GetProperty("eventId").GetGuid());
        Assert.Equal(journeyEvent.SequenceNumber, root.GetProperty("sequenceNumber").GetInt64());
        Assert.Equal("delay_published", root.GetProperty("type").GetString());
        Assert.Equal(journeyEvent.JourneyId, root.GetProperty("journeyId").GetGuid());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("stopSequence").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("stationId").ValueKind);
        Assert.Equal(15, root.GetProperty("values").GetProperty("delayMinutes").GetInt32());
        Assert.Equal(journeyEvent.OccurredAt, root.GetProperty("publishedAt").GetDateTimeOffset());
        Assert.Equal("automatically_published", root.GetProperty("provenance").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("correctsEventId").ValueKind);
    }

    [Fact]
    public async Task A_platform_change_names_the_most_recently_changed_stop_and_its_platform_number()
    {
        // Arrange
        var stationId = Guid.CreateVersion7();
        var oldPlatformId = Guid.CreateVersion7();
        var newPlatformId = Guid.CreateVersion7();
        var station = TestData.Station(stationId, oldPlatformId, Guid.CreateVersion7()) with
        {
            Platforms =
            [
                new(oldPlatformId, stationId, "1"),
                new(newPlatformId, stationId, "2"),
            ],
        };
        var stops = new[]
        {
            TestData.Stop(1, stationId, TestData.Now, oldPlatformId, platformChangedAt: TestData.Now.AddMinutes(-5)),
            TestData.Stop(2, stationId, TestData.Now.AddMinutes(10), newPlatformId, platformChangedAt: TestData.Now),
        };
        var journeyEvent = NewEvent(JourneyEventType.PlatformChanged, stops: stops);
        var factory = new WebhookPayloadFactory(new StubNetworkCatalog([station]));

        // Act
        var json = await factory.BuildAsync(
            WebhookEventType.PlatformChanged,
            journeyEvent,
            CancellationToken.None);

        // Assert
        using var payload = JsonDocument.Parse(json);
        var root = payload.RootElement;
        Assert.Equal(2, root.GetProperty("stopSequence").GetInt32());
        Assert.Equal(stationId, root.GetProperty("stationId").GetGuid());
        Assert.Equal(newPlatformId, root.GetProperty("values").GetProperty("platformId").GetGuid());
        Assert.Equal("2", root.GetProperty("values").GetProperty("platformNumber").GetString());
    }

    [Fact]
    public async Task A_termination_names_the_last_stop_with_an_actual_departure()
    {
        // Arrange
        var firstStationId = Guid.CreateVersion7();
        var lastServedStationId = Guid.CreateVersion7();
        var stops = new[]
        {
            TestData.Stop(1, firstStationId, TestData.Now, actualDeparture: TestData.Now.AddMinutes(1)),
            TestData.Stop(2, lastServedStationId, TestData.Now.AddMinutes(10), actualDeparture: TestData.Now.AddMinutes(11)),
            TestData.Stop(3, Guid.CreateVersion7(), TestData.Now.AddMinutes(20)),
        };
        var journeyEvent = NewEvent(JourneyEventType.TerminationPublished, stops: stops);
        var factory = new WebhookPayloadFactory(new StubNetworkCatalog());

        // Act
        var json = await factory.BuildAsync(
            WebhookEventType.TerminationPublished,
            journeyEvent,
            CancellationToken.None);

        // Assert
        using var payload = JsonDocument.Parse(json);
        var root = payload.RootElement;
        Assert.Equal(2, root.GetProperty("stopSequence").GetInt32());
        Assert.Equal(lastServedStationId, root.GetProperty("stationId").GetGuid());
        Assert.Empty(root.GetProperty("values").EnumerateObject());
    }

    [Fact]
    public async Task A_correction_names_the_withdrawn_event_and_has_no_additional_values()
    {
        // Arrange
        var correctsEventId = Guid.CreateVersion7();
        var journeyEvent = NewEvent(JourneyEventType.CorrectionPublished, correctsEventId: correctsEventId);
        var factory = new WebhookPayloadFactory(new StubNetworkCatalog());

        // Act
        var json = await factory.BuildAsync(
            WebhookEventType.CorrectionPublished,
            journeyEvent,
            CancellationToken.None);

        // Assert
        using var payload = JsonDocument.Parse(json);
        var root = payload.RootElement;
        Assert.Equal("correction_published", root.GetProperty("type").GetString());
        Assert.Equal(correctsEventId, root.GetProperty("correctsEventId").GetGuid());
        Assert.Empty(root.GetProperty("values").EnumerateObject());
    }

    [Fact]
    public async Task A_missing_platform_in_the_current_catalogue_produces_a_null_platform_number()
    {
        // Arrange
        var stationId = Guid.CreateVersion7();
        var platformId = Guid.CreateVersion7();
        var stops = new[]
        {
            TestData.Stop(1, stationId, TestData.Now, platformId, platformChangedAt: TestData.Now),
        };
        var journeyEvent = NewEvent(JourneyEventType.PlatformChanged, stops: stops);
        var factory = new WebhookPayloadFactory(new StubNetworkCatalog());

        // Act
        var json = await factory.BuildAsync(
            WebhookEventType.PlatformChanged,
            journeyEvent,
            CancellationToken.None);

        // Assert
        using var payload = JsonDocument.Parse(json);
        Assert.Equal(
            JsonValueKind.Null,
            payload.RootElement.GetProperty("values").GetProperty("platformNumber").ValueKind);
    }

    [Fact]
    public async Task An_unknown_webhook_event_type_is_rejected()
    {
        // Arrange
        var factory = new WebhookPayloadFactory(new StubNetworkCatalog());
        var journeyEvent = NewEvent(JourneyEventType.DelayPublished);

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            factory.BuildAsync((WebhookEventType)99, journeyEvent, CancellationToken.None));

        // Assert
        Assert.Equal("eventType", exception.ParamName);
    }

    [Fact]
    public async Task A_missing_journey_event_is_rejected()
    {
        // Arrange
        var factory = new WebhookPayloadFactory(new StubNetworkCatalog());

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            factory.BuildAsync(WebhookEventType.DelayPublished, null!, CancellationToken.None));

        // Assert
        Assert.Equal("journeyEvent", exception.ParamName);
    }

    private static JourneyEventView NewEvent(
        JourneyEventType type,
        int? delayMinutes = null,
        IReadOnlyList<LiveStopView>? stops = null,
        Guid? correctsEventId = null)
    {
        var stationId = Guid.CreateVersion7();
        stops ??= [TestData.Stop(1, stationId, TestData.Now)];
        var journey = TestData.Journey(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            stops,
            delayMinutes: delayMinutes);
        return TestData.Event(type, journey, correctsEventId);
    }
}
