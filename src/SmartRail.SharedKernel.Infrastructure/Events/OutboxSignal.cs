using System.Threading.Channels;

namespace SmartRail.SharedKernel.Infrastructure.Events;

/// <summary>
/// Wakes the dispatcher when a module saves its outbox (design D7). A singleton, set after
/// <c>SaveChanges</c> through <see cref="OutboxSignalInterceptor"/>; the dispatcher also polls, so a
/// signal that finds nothing yet (a save before the final commit) only delays delivery to that poll.
/// </summary>
internal sealed class OutboxSignal
{
    private readonly Channel<bool> signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    /// <summary>Signals that there may be something to dispatch. Never blocks.</summary>
    public void Notify() => signal.Writer.TryWrite(true);

    /// <summary>Waits for a signal or for <paramref name="timeout"/>, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await signal.Reader.ReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The poll interval elapsed first; the next cycle reads the outbox anyway.
        }
    }
}
