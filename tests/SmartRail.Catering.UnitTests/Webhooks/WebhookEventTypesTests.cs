using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Domain.Webhooks;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Catering.UnitTests.Webhooks;

public sealed class WebhookEventTypesTests
{
    [Theory]
    [InlineData(JourneyEventType.DelayPublished, WebhookEventType.DelayPublished)]
    [InlineData(JourneyEventType.CancellationPublished, WebhookEventType.CancellationPublished)]
    [InlineData(JourneyEventType.TerminationPublished, WebhookEventType.TerminationPublished)]
    [InlineData(JourneyEventType.PlatformChanged, WebhookEventType.PlatformChanged)]
    [InlineData(JourneyEventType.CorrectionPublished, WebhookEventType.CorrectionPublished)]
    public void Happy_path_maps_every_pushed_journey_event(
        JourneyEventType journeyEventType,
        WebhookEventType expected)
    {
        // Arrange

        // Act
        var mapped = WebhookEventTypes.TryMap(journeyEventType, out var actual);

        // Assert
        Assert.True(mapped);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(JourneyEventType.DepartureLogged)]
    [InlineData(JourneyEventType.JourneyCompleted)]
    public void An_event_that_is_not_pushed_has_no_webhook_mapping(JourneyEventType journeyEventType)
    {
        // Arrange

        // Act
        var mapped = WebhookEventTypes.TryMap(journeyEventType, out _);

        // Assert
        Assert.False(mapped);
    }

    [Theory]
    [InlineData(WebhookEventType.DelayPublished, "delay_published")]
    [InlineData(WebhookEventType.CancellationPublished, "cancellation_published")]
    [InlineData(WebhookEventType.TerminationPublished, "termination_published")]
    [InlineData(WebhookEventType.PlatformChanged, "platform_changed")]
    [InlineData(WebhookEventType.CorrectionPublished, "correction_published")]
    public void Every_pushed_event_has_its_snake_case_storage_name(
        WebhookEventType eventType,
        string expected)
    {
        // Arrange

        // Act
        var actual = WebhookEventTypes.ToStorageValue(eventType);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void An_unknown_webhook_event_type_has_no_storage_name()
    {
        // Arrange
        const WebhookEventType unknown = (WebhookEventType)99;

        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebhookEventTypes.ToStorageValue(unknown));

        // Assert
        Assert.Equal("eventType", exception.ParamName);
    }
}
