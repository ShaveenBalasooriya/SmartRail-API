using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Application.Webhooks.RegisterWebhook;
using SmartRail.Catering.Domain.Webhooks;
using SmartRail.Catering.UnitTests.TestDoubles;

namespace SmartRail.Catering.UnitTests.Webhooks.RegisterWebhook;

public sealed class RegisterWebhookHandlerTests
{
    [Fact]
    public async Task Happy_path_adds_and_saves_the_providers_first_subscription()
    {
        // Arrange
        var providerId = Guid.CreateVersion7();
        var subscriptions = new FakeWebhookSubscriptionRepository();
        var handler = HandlerDriver.CreateCommandHandler<RegisterWebhookCommand, WebhookRegistration>(subscriptions, new StubTimeProvider(TestData.Now));
        var command = new RegisterWebhookCommand(providerId, "https://provider.example.com/events");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        var stored = Assert.Single(subscriptions.Added);
        Assert.Equal(providerId, stored.ProviderId);
        Assert.Equal(command.Url, result.Value.Subscription.Url);
        Assert.Equal(stored.Id, result.Value.Subscription.Id);
        Assert.True(result.Value.Subscription.IsActive);
        Assert.Equal(TestData.Now, result.Value.Subscription.CreatedAt);
        Assert.Equal(1, subscriptions.SaveChangesCount);
    }

    [Fact]
    public async Task Registering_again_replaces_and_reactivates_the_existing_subscription()
    {
        // Arrange
        var providerId = Guid.CreateVersion7();
        var existing = NewSubscription(providerId);
        existing.Deactivate();
        var subscriptions = new FakeWebhookSubscriptionRepository(existing);
        var replacedAt = TestData.Now.AddDays(1);
        var handler = HandlerDriver.CreateCommandHandler<RegisterWebhookCommand, WebhookRegistration>(subscriptions, new StubTimeProvider(replacedAt));
        var command = new RegisterWebhookCommand(providerId, "https://provider.example.com/replacement");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Created);
        Assert.Empty(subscriptions.Added);
        Assert.Same(existing, subscriptions.Subscription);
        Assert.Equal(command.Url, existing.CallbackUrl.Value);
        Assert.True(existing.IsActive);
        Assert.Equal(replacedAt, existing.CreatedAt);
        Assert.Equal(1, subscriptions.SaveChangesCount);
    }

    [Fact]
    public async Task An_empty_provider_identifier_is_rejected_without_adding_or_saving()
    {
        // Arrange
        var subscriptions = new FakeWebhookSubscriptionRepository();
        var handler = HandlerDriver.CreateCommandHandler<RegisterWebhookCommand, WebhookRegistration>(subscriptions, new StubTimeProvider(TestData.Now));

        // Act
        var result = await handler.Handle(
            new RegisterWebhookCommand(Guid.Empty, "https://provider.example.com/events"),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(WebhookSubscriptionErrors.ProviderRequired, result.Error);
        Assert.Empty(subscriptions.Added);
        Assert.Equal(0, subscriptions.SaveChangesCount);
    }

    [Fact]
    public async Task An_invalid_replacement_is_rejected_without_saving_or_changing_the_existing_url()
    {
        // Arrange
        var providerId = Guid.CreateVersion7();
        var existing = NewSubscription(providerId);
        var originalUrl = existing.CallbackUrl;
        var subscriptions = new FakeWebhookSubscriptionRepository(existing);
        var handler = HandlerDriver.CreateCommandHandler<RegisterWebhookCommand, WebhookRegistration>(subscriptions, new StubTimeProvider(TestData.Now));

        // Act
        var result = await handler.Handle(
            new RegisterWebhookCommand(providerId, "not-a-url"),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(originalUrl, existing.CallbackUrl);
        Assert.Empty(subscriptions.Added);
        Assert.Equal(0, subscriptions.SaveChangesCount);
    }

    [Fact]
    public async Task A_repository_add_failure_is_propagated_before_any_save()
    {
        // Arrange
        var subscriptions = new FakeWebhookSubscriptionRepository
        {
            ExceptionToThrowOnAdd = new InvalidOperationException("Store unavailable."),
        };
        var handler = HandlerDriver.CreateCommandHandler<RegisterWebhookCommand, WebhookRegistration>(subscriptions, new StubTimeProvider(TestData.Now));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new RegisterWebhookCommand(Guid.CreateVersion7(), "https://provider.example.com/events"),
            CancellationToken.None));

        // Assert
        Assert.Equal("Store unavailable.", exception.Message);
        Assert.Empty(subscriptions.Added);
        Assert.Equal(0, subscriptions.SaveChangesCount);
    }

    private static WebhookSubscription NewSubscription(Guid providerId) =>
        WebhookSubscription.Create(
            providerId,
            "https://provider.example.com/original",
            TestData.Now).Value;
}
