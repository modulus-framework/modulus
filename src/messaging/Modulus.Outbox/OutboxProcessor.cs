using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Modulus.Outbox;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;

public sealed class OutboxProcessor(
    IServiceProvider         sp,
    IOptions<OutboxOptions>  opts,
    ILogger<OutboxProcessor> logger)
{
    public async Task ProcessAsync(CancellationToken ct = default)
    {
        await using var scope      = sp.CreateAsyncScope();
        var db                     = scope.ServiceProvider.GetRequiredService<DbContext>();
        var dispatcher             = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
        var options                = opts.Value;

        var messages = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null
                     && m.RetryCount  <  options.MaxRetries)
            .OrderBy(m => m.CreatedAt)
            .Take(options.BatchSize)
            .ToListAsync(ct);

        if (messages.Count == 0) return;

        foreach (var message in messages)
        {
            try
            {
                await dispatcher.DispatchAsync(message, ct);
                message.ProcessedAt = DateTime.UtcNow;
                logger.LogDebug("Outbox dispatched {Id} ({Type})",
                    message.Id, message.MessageType);
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.Error = ex.Message;
                logger.LogWarning(ex,
                    "Outbox dispatch failed for {Id} (attempt {N})",
                    message.Id, message.RetryCount);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}