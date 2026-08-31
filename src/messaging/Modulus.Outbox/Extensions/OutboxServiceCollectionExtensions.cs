using Microsoft.Extensions.DependencyInjection;

namespace Modulus.Outbox.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.Outbox.Abstractions;
using Modulus.Outbox.Dispatchers;

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddOutbox<TContext>(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null)
        where TContext : DbContext
    {
        // Merge every AddOutbox call into ONE options instance. Registering
        // Options.Create directly would make the last module's configuration
        // silently discard every earlier module's settings.
        services.AddOptions<OutboxOptions>().Configure(o => configure?.Invoke(o));

        // Writer uses the scoped TContext
        services.AddScoped<DbContext>(
            sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();

        // Dispatcher (default: in-process)
        services.AddScoped<IOutboxDispatcher, InProcessOutboxDispatcher>();

        // Processor: scoped (one per polling iteration). Required by
        // OutboxPollingService; was previously missing — the hosted service
        // could not resolve it.
        services.AddScoped<OutboxProcessor>();

        // Polling hosted service. AddHostedService is TryAddEnumerable, so
        // multiple AddOutbox calls register it only once; DisableAutoPolling is
        // honoured at runtime (see OutboxPollingService) because the final
        // merged options are not known until the container is built.
        services.AddHostedService<OutboxPollingService>();

        return services;
    }
}
