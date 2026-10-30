using System.Net.Http;
using System.Text;
using SmartRail.Catering.Application.Webhooks;

namespace SmartRail.Catering.Infrastructure.Webhooks;

/// <summary>The outcome of one POST to a provider's callback URL.</summary>
/// <param name="Delivered">True when the receiver answered 2xx.</param>
/// <param name="StatusCode">The response status, or <see langword="null"/> on a network error or timeout.</param>
internal sealed record WebhookSendResult(bool Delivered, int? StatusCode);

/// <summary>
/// Posts a payload to the callback URL (design D2). The client has the configured timeout and does
/// not follow redirects, and a network error or timeout is a failed attempt rather than an exception
/// the dispatcher has to interpret (webhook-delivery spec, "Retry with backoff"; design D4).
/// </summary>
internal interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(
        string callbackUrl,
        string payload,
        CancellationToken cancellationToken);
}

/// <summary>
/// The one HTTP client the module posts with. It is a singleton, so connections are reused, and it is
/// registered with the dispatcher options, so a test can shorten its timeout.
/// </summary>
internal sealed class WebhookSender(WebhookDispatcherOptions options) : IWebhookSender, IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
    };

    public async Task<WebhookSendResult> SendAsync(
        string callbackUrl,
        string payload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackUrl);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(callbackUrl, content, cancellationToken);

            return new WebhookSendResult(response.IsSuccessStatusCode, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            // The client's timeout elapsed: a failed attempt, retried on the backoff schedule.
            return new WebhookSendResult(false, null);
        }
        catch (HttpRequestException)
        {
            return new WebhookSendResult(false, null);
        }
    }

    public void Dispose() => client.Dispose();
}
