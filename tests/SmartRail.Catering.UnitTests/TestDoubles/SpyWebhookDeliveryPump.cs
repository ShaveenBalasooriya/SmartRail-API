using SmartRail.Catering.Application.Webhooks;

namespace SmartRail.Catering.UnitTests.TestDoubles;

/// <summary>A pump spy that returns a configured attempt count.</summary>
internal sealed class SpyWebhookDeliveryPump(int result = 0) : IWebhookDeliveryPump
{
    public int CallCount { get; private set; }

    public Exception? ExceptionToThrow { get; set; }

    public Task<int> PumpAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(result);
    }
}
