using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Application.Webhooks.GetWebhook;
using SmartRail.Catering.Domain.Webhooks;
using SmartRail.Catering.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.UnitTests.Webhooks.GetWebhook;

public sealed class GetWebhookHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_the_providers_active_subscription()
    {
        // Arrange
        var providerId = Guid.CreateVersion7();
        var subscription = NewSubscription(providerId);
        var handler = HandlerDriver.CreateQueryHandler<GetWebhookQuery, WebhookSubscriptionResponse>(new FakeWebhookSubscriptionRepository(subscription));

        // Act
        var result = await handler.Handle(new GetWebhookQuery(providerId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(subscription.Id, result.Value.Id);
        Assert.Equal(subscription.CallbackUrl.Value, result.Value.Url);
        Assert.True(result.Value.IsActive);
        Assert.Equal(subscription.CreatedAt, result.Value.CreatedAt);
    }

    [Fact]
    public async Task A_provider_without_a_subscription_receives_not_found()
    {
        // Arrange
        var handler = HandlerDriver.CreateQueryHandler<GetWebhookQuery, WebhookSubscriptionResponse>(new FakeWebhookSubscriptionRepository());

        // Act
        var result = await handler.Handle(new GetWebhookQuery(Guid.CreateVersion7()), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task An_inactive_subscription_receives_not_found()
    {
        // Arrange
        var providerId = Guid.CreateVersion7();
        var subscription = NewSubscription(providerId);
        subscription.Deactivate();
        var handler = HandlerDriver.CreateQueryHandler<GetWebhookQuery, WebhookSubscriptionResponse>(new FakeWebhookSubscriptionRepository(subscription));

        // Act
        var result = await handler.Handle(new GetWebhookQuery(providerId), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    private static WebhookSubscription NewSubscription(Guid providerId) =>
        WebhookSubscription.Create(
            providerId,
            "https://provider.example.com/events",
            TestData.Now).Value;
}
