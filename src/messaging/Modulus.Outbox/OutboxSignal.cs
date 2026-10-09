namespace Modulus.Outbox;

using System.Threading.Channels;
using Modulus.Outbox.Abstractions;

/// <summary>In-process <see cref="IOutboxSignal"/>: a one-slot channel, so any number of notifications collapse into one wake-up.</summary>
public sealed class OutboxSignal : IOutboxSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <inheritdoc />
    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>Waits for a notification or until <paramref name="timeout"/> passes, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // interval elapsed
        }
    }
}
