using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Modulus.Messaging.Outbox.Npgsql;

/// <summary>Runs <see cref="NpgsqlOutboxDispatch"/> in a loop: back-to-back while there is work, else once per poll interval.</summary>
internal sealed class NpgsqlOutboxDispatcherService(
    IServiceProvider services,
    NpgsqlDataSource dataSource,
    NpgsqlOutboxOptions options,
    ILogger logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var transport = services.GetRequiredService<IMessageTransport>();
        if (options.CreateSchema)
        {
            await using var schema = dataSource.CreateCommand(NpgsqlOutboxSql.Schema(options.Table));
            await schema.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;
            try
            {
                sent = await NpgsqlOutboxDispatch.DispatchOnceAsync(dataSource, transport, options, stoppingToken).ConfigureAwait(false);
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
