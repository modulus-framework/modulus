using Microsoft.Extensions.DependencyInjection;

namespace Modulus.Outbox.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Events.Abstractions;
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

        // Replace the NullIntegrationEventOutbox (registered by
        // AddModuleDatabase) with the EF Core writer so that
        // ModuleDbContext enqueues integration events transactionally.
        services.Replace(ServiceDescriptor.Scoped<
            IIntegrationEventOutbox, EfOutboxWriter>());

        // Dispatcher (default: in-process)
        services.AddScoped<IOutboxDispatcher, InProcessOutboxDispatcher>();

        // Processor: scoped (one per polling iteration). Required by
        // OutboxPollingService; was previously missing — the hosted service
        // could not resolve it.
        services.AddScoped<OutboxProcessor>();

        // Processor (register as hosted service unless disabled)
        if (!opts.DisableAutoPolling)
            services.AddHostedService<OutboxPollingService>();

        return services;
    }
}