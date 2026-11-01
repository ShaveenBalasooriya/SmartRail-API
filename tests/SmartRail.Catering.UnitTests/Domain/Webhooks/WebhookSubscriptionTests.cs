using SmartRail.Catering.Domain.Webhooks;
using SmartRail.Catering.UnitTests.TestDoubles;

namespace SmartRail.Catering.UnitTests.Domain.Webhooks;

public sealed class WebhookSubscriptionTests
{
    [Fact]
    public void Happy_path_creates_an_active_subscription_for_the_provider()
    {
        // Arrange
        var providerId = Guid.CreateVersion7();

        // Act
        var result = WebhookSubscription.Create(
            providerId,
            "https://provider.example.com/events",
            TestData.Now);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
        Assert.Equal(providerId, result.Value.ProviderId);
        Assert.Equal("https://provider.example.com/events", result.Value.CallbackUrl.Value);
        Assert.True(result.Value.IsActive);
        Assert.Equal(TestData.Now, result.Value.CreatedAt);
    }

    [Fact]
    public void Replacing_the_url_reactivates_the_same_subscription_and_restarts_its_event_window()
    {
        // Arrange
        var subscription = NewSubscription();
        subscription.Deactivate();
        var replacedAt = TestData.Now.AddDays(1);

        // Act
        var result = subscription.Replace("https://provider.example.com/new-events", replacedAt);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("https://provider.example.com/new-events", subscription.CallbackUrl.Value);
        Assert.True(subscription.IsActive);
        Assert.Equal(replacedAt, subscription.CreatedAt);
    }

    [Fact]
    public void Deactivating_stops_the_subscription_being_active()
    {
        // Arrange
        var subscription = NewSubscription();

        // Act
        subscription.Deactivate();

        // Assert
        Assert.False(subscription.IsActive);
    }

    [Fact]
    public void An_empty_provider_identifier_is_rejected()
    {
        // Arrange

        // Act
        var result = WebhookSubscription.Create(
            Guid.Empty,
            "https://provider.example.com/events",
            TestData.Now);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(WebhookSubscriptionErrors.ProviderRequired, result.Error);
    }

    [Fact]
    public void An_invalid_replacement_leaves_the_existing_url_and_activation_unchanged()
    {
        // Arrange
        var subscription = NewSubscription();
        var originalUrl = subscription.CallbackUrl;
        var originalCreatedAt = subscription.CreatedAt;

        // Act
        var result = subscription.Replace("not-a-url", TestData.Now.AddDays(1));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(originalUrl, subscription.CallbackUrl);
        Assert.True(subscription.IsActive);
        Assert.Equal(originalCreatedAt, subscription.CreatedAt);
    }

    private static WebhookSubscription NewSubscription() =>
        WebhookSubscription.Create(
            Guid.CreateVersion7(),
            "https://provider.example.com/events",
            TestData.Now).Value;
}
