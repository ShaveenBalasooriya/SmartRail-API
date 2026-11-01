using System.Net;
using SmartRail.Api.Tests.Infrastructure;

namespace SmartRail.Api.Tests.Provider;

/// <summary>
/// <c>PUT /provider/webhook</c> and <c>DELETE /provider/webhook</c>: the catering provider keeps one
/// callback registration (EP-12; M-26's webhook subscription). A provider can only ever reach its own
/// subscription, because the API key identifies it and no route names a provider.
/// </summary>
[Collection(ApiDatabase.Name)]
public sealed class ProviderWebhookTests(ApiDatabaseFixture database) : ApiTest(database)
{
    private const string CallbackUrl = "https://rail-bites.example/hooks/smartrail";

    [ApiFact]
    public async Task Happy_path_the_first_registration_creates_the_subscription()
    {
        // Act
        using var response = await Api.PutAsync(
            Urls.ProviderWebhook,
            Api.Provider,
            new { url = CallbackUrl });

        // Assert
        await response.HasStatusAsync(HttpStatusCode.Created);
        var subscription = await response.ReadAsync<WebhookSubscription>();

        Assert.NotEqual(Guid.Empty, subscription.Id);
        Assert.Equal(CallbackUrl, subscription.Url);
        Assert.True(subscription.IsActive, "A new registration is not active.");

        // The registration is readable through the API, so a restart of the provider can find it.
        using var stored = await Api.GetAsync(Urls.ProviderWebhook, Api.Provider);
        await stored.HasStatusAsync(HttpStatusCode.OK);

        var reread = await stored.ReadAsync<WebhookSubscription>();
        Assert.Equal(subscription.Id, reread.Id);
        Assert.Equal(CallbackUrl, reread.Url);
    }

    [ApiFact]
    public async Task Registering_again_replaces_the_url_and_keeps_one_subscription()
    {
        // Arrange
        Guid subscriptionId;
        using (var created = await Api.PutAsync(Urls.ProviderWebhook, Api.Provider, new { url = CallbackUrl }))
        {
            await created.HasStatusAsync(HttpStatusCode.Created);
            subscriptionId = (await created.ReadAsync<WebhookSubscription>()).Id;
        }

        var replacement = "https://rail-bites.example/hooks/smartrail-v2";

        // Act
        using var response = await Api.PutAsync(
            Urls.ProviderWebhook,
            Api.Provider,
            new { url = replacement });

        // Assert
        await response.HasStatusAsync(HttpStatusCode.OK);
        var subscription = await response.ReadAsync<WebhookSubscription>();

        Assert.Equal(subscriptionId, subscription.Id);
        Assert.Equal(replacement, subscription.Url);
        Assert.True(subscription.IsActive, "The replacement registration is not active.");
    }

    [ApiFact]
    public async Task A_registration_with_a_url_that_is_not_absolute_is_a_bad_request()
    {
        // Act
        using var response = await Api.PutAsync(
            Urls.ProviderWebhook,
            Api.Provider,
            new { url = "not-a-url" });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.BadRequest);

        // Nothing was registered.
        using var stored = await Api.GetAsync(Urls.ProviderWebhook, Api.Provider);
        await stored.IsProblemAsync(HttpStatusCode.NotFound);
    }

    [ApiFact]
    public async Task Happy_path_deactivating_the_subscription_answers_no_content()
    {
        // Arrange
        using (var created = await Api.PutAsync(Urls.ProviderWebhook, Api.Provider, new { url = CallbackUrl }))
        {
            await created.HasStatusAsync(HttpStatusCode.Created);
        }

        // Act
        using var response = await Api.DeleteAsync(Urls.ProviderWebhook, Api.Provider);

        // Assert
        await response.HasStatusAsync(HttpStatusCode.NoContent);

        // The provider has no active subscription any more.
        using var stored = await Api.GetAsync(Urls.ProviderWebhook, Api.Provider);
        await stored.IsProblemAsync(HttpStatusCode.NotFound);
    }

    [ApiFact]
    public async Task Deactivating_a_subscription_that_was_never_registered_is_not_found()
    {
        // Act
        using var response = await Api.DeleteAsync(Urls.ProviderWebhook, Api.Provider);

        // Assert
        await response.IsProblemAsync(HttpStatusCode.NotFound);
    }
}
