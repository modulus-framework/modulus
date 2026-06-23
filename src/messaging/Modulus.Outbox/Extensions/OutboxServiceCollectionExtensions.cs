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
        var opts = new OutboxOptions();
        configure?.Invoke(opts);
        services.AddSingleton(Options.Create(opts));

        // Writer uses the scoped TContext
        services.AddScoped<DbContext>(
            sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();

        // Dispatcher (default: in-process)
        services.AddScoped<IOutboxDispatcher, InProcessOutboxDispatcher>();

        // Processor (register as hosted service unless disabled)
        if (!opts.DisableAutoPolling)
            services.AddHostedService<OutboxPollingService>();

        return services;
    }
}