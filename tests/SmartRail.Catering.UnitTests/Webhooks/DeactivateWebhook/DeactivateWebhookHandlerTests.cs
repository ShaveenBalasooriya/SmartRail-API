using SmartRail.Catering.Application.Webhooks.DeactivateWebhook;
using SmartRail.Catering.Domain.Webhooks;
using SmartRail.Catering.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.UnitTests.Webhooks.DeactivateWebhook;

public sealed class DeactivateWebhookHandlerTests
{
    [Fact]
    public async Task Happy_path_deactivates_and_saves_the_providers_subscription()
    {
        // Arrange
        var providerId = Guid.CreateVersion7();
        var subscription = NewSubscription(providerId);
        var subscriptions = new FakeWebhookSubscriptionRepository(subscription);
        var handler = HandlerDriver.CreateCommandHandler<DeactivateWebhookCommand>(subscriptions);

        // Act
        var result = await handler.Handle(new DeactivateWebhookCommand(providerId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(subscription.IsActive);
        Assert.Equal(1, subscriptions.SaveChangesCount);
    }

    [Fact]
    public async Task A_missing_subscription_returns_not_found_without_a_save()
    {
        // Arrange
        var subscriptions = new FakeWebhookSubscriptionRepository();
        var handler = HandlerDriver.CreateCommandHandler<DeactivateWebhookCommand>(subscriptions);

        // Act
        var result = await handler.Handle(new DeactivateWebhookCommand(Guid.CreateVersion7()), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, subscriptions.SaveChangesCount);
    }

    [Fact]
    public async Task An_already_inactive_subscription_returns_not_found_without_another_save()
    {
        // Arrange
        var providerId = Guid.CreateVersion7();
        var subscription = NewSubscription(providerId);
        subscription.Deactivate();
        var subscriptions = new FakeWebhookSubscriptionRepository(subscription);
        var handler = HandlerDriver.CreateCommandHandler<DeactivateWebhookCommand>(subscriptions);

        // Act
        var result = await handler.Handle(new DeactivateWebhookCommand(providerId), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, subscriptions.SaveChangesCount);
    }

    [Fact]
    public async Task A_save_failure_is_propagated()
    {
        // Arrange
        var providerId = Guid.CreateVersion7();
        var subscription = NewSubscription(providerId);
        var subscriptions = new FakeWebhookSubscriptionRepository(subscription)
        {
            ExceptionToThrowOnSave = new InvalidOperationException("Store unavailable."),
        };
        var handler = HandlerDriver.CreateCommandHandler<DeactivateWebhookCommand>(subscriptions);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeactivateWebhookCommand(providerId), CancellationToken.None));

        // Assert
        Assert.Equal("Store unavailable.", exception.Message);
        Assert.Equal(1, subscriptions.SaveChangesCount);
    }

    private static WebhookSubscription NewSubscription(Guid providerId) =>
        WebhookSubscription.Create(
            providerId,
            "https://provider.example.com/events",
            TestData.Now).Value;
}
