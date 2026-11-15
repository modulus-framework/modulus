using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Modulus.Messaging.Outbox.EntityFrameworkCore;

/// <summary>Runs <see cref="OutboxDispatch"/> in a loop: back-to-back while there is work, else once per poll interval.</summary>
internal sealed class OutboxDispatcherService<TContext>(
    IServiceScopeFactory scopes,
    EfOutboxOptions options,
    ILogger<OutboxDispatcherService<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TContext>();
                var transport = scope.ServiceProvider.GetRequiredService<IMessageTransport>();
                sent = await OutboxDispatch.DispatchOnceAsync(db, transport, options.BatchSize, options.LeaseDuration, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Rows stay in the table; their lease expires and the next round retries them.
                logger.LogError(exception, "Outbox dispatch round failed; retrying after the poll interval.");
            }

            if (sent > 0)
            {
                continue;
            }

            try
            {
                await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
