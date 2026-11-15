using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Modulus.Messaging.Outbox.Npgsql;

public static class MessagingNpgsqlOutboxExtensions
{
    /// <summary>
    /// Registers the PostgreSQL unit of work: one <see cref="NpgsqlUnitOfWork"/> per scope, shared by
    /// <see cref="INpgsqlUnitOfWork"/> and the consume attempt's <see cref="IConsumeUnitOfWork"/>. The application needs an
    /// <see cref="NpgsqlDataSource"/> in its services. Call it after <c>AddModulusMessaging</c>.
    /// </summary>
    public static IServiceCollection AddModulusMessagingNpgsqlUnitOfWork(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped(sp => new NpgsqlUnitOfWork(sp.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddScoped<INpgsqlUnitOfWork>(sp => sp.GetRequiredService<NpgsqlUnitOfWork>());
        services.TryAddScoped<IConsumeUnitOfWork>(sp => sp.GetRequiredService<NpgsqlUnitOfWork>());
        return services;
    }

    /// <summary>
    /// Stores outgoing messages in the scope's PostgreSQL transaction (see <see cref="INpgsqlUnitOfWork"/>), and sends them
    /// from a dispatcher once it commits. Publishes and sends from a scope then take effect only when that transaction
    /// commits. Call after <c>AddModulusMessaging</c>.
    /// </summary>
    public static IServiceCollection AddModulusMessagingNpgsqlOutbox(this IServiceCollection services, Action<NpgsqlOutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new NpgsqlOutboxOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddModulusMessagingNpgsqlUnitOfWork();
        services.AddScoped<IMessageOutbox>(sp => new NpgsqlMessageOutbox(sp.GetRequiredService<INpgsqlUnitOfWork>(), options.Table));
        if (options.RunDispatcher)
        {
            services.AddSingleton<IHostedService>(sp => new NpgsqlOutboxDispatcherService(
                sp,
                sp.GetRequiredService<NpgsqlDataSource>(),
                options,
                sp.GetService<ILoggerFactory>()?.CreateLogger("Modulus.Messaging.Outbox.Npgsql") ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
        }

        return services;
    }

    /// <summary>The SQL that creates the outbox table and its indexes, for a migration to run instead of <c>CreateSchema</c>.</summary>
    public static string OutboxSchemaScript(string table = "messaging_outbox") => NpgsqlOutboxSql.Schema(table);
}
