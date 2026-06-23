using Modulus.Outbox.Abstractions;

namespace Modulus.Outbox;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

public sealed class OutboxPollingService(
    OutboxProcessor             processor,
    IOptions<OutboxOptions>     opts)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(opts.Value.PollingIntervalSec);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
